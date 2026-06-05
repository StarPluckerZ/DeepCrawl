import asyncio
import hashlib
import logging
import os
import time
from collections import OrderedDict
from urllib.parse import urlparse

from cloakbrowser import launch_async

logger = logging.getLogger(__name__)

MAX_CONCURRENT = int(os.getenv("CLOAKBROWSER_MAX_CONCURRENT", "3"))
DEFAULT_TIMEOUT_MS = int(os.getenv("CLOAKBROWSER_TIMEOUT_MS", "30000"))
MAX_BROWSERS = int(os.getenv("CLOAKBROWSER_MAX_BROWSERS", str(MAX_CONCURRENT)))
EXTRA_WAIT_MS = int(os.getenv("CLOAKBROWSER_EXTRA_WAIT_MS", "2000"))
CONTENT_WAIT_MS = int(os.getenv("CLOAKBROWSER_CONTENT_WAIT_MS", "15000"))
CONTENT_INTERVAL_MS = int(os.getenv("CLOAKBROWSER_CONTENT_INTERVAL_MS", "500"))
STABLE_COUNT = int(os.getenv("CLOAKBROWSER_STABLE_COUNT", "5"))

VALID_WAIT_UNTIL = {"load", "networkidle", "domcontentloaded", "commit"}

_browsers: OrderedDict[str, object] = OrderedDict()
_semaphore = asyncio.Semaphore(MAX_CONCURRENT)
_browsers_lock = asyncio.Lock()


def _compute_seed(domain: str) -> str:
    h = hashlib.sha256(domain.encode()).hexdigest()[:8]
    return str(int(h, 16) % 100_000)


def _extract_domain(url: str) -> str:
    return urlparse(url).hostname or url


def _is_http_url(url: str) -> bool:
    try:
        parsed = urlparse(url)
        return parsed.scheme in ("http", "https") and bool(parsed.netloc)
    except Exception:
        return False


async def _wait_for_content(page):
    start = time.monotonic()
    stable = 0
    max_len = 0
    while True:
        cur = await page.evaluate("() => document.body.innerText.length")
        if cur > max_len:
            max_len = cur
            stable = 0
        elif cur > 0:
            stable += 1
            if stable >= STABLE_COUNT:
                return
        if (time.monotonic() - start) * 1000 >= CONTENT_WAIT_MS:
            break
        await page.wait_for_timeout(CONTENT_INTERVAL_MS)


async def get_browser(seed: str, proxy: str | None = None):
    key = f"{seed}:proxy" if proxy else seed
    async with _browsers_lock:
        if key in _browsers:
            _browsers.move_to_end(key)
            return _browsers[key]

        while len(_browsers) >= MAX_BROWSERS:
            evict_key, evict_browser = _browsers.popitem(last=False)
            await evict_browser.close()
            logger.info("Evicted browser %s (max_browsers=%d)", evict_key, MAX_BROWSERS)

        kwargs = dict(humanize=True, geoip=True,
                      args=[f"--fingerprint={seed}", "--blink-settings=imagesEnabled=false"])
        if proxy:
            kwargs["proxy"] = proxy
        browser = await launch_async(**kwargs)
        _browsers[key] = browser
        logger.info("CloakBrowser launched (key=%s, active_browsers=%d)", key, len(_browsers))
        return browser


async def fetch_html(url: str, wait_until: str | None = None, proxy: str | None = None) -> str:
    if not _is_http_url(url):
        raise InvalidUrlError(f"Invalid URL: {url}")

    domain = _extract_domain(url)
    seed = _compute_seed(domain)
    browser = await get_browser(seed, proxy)

    wait_strategy = _resolve_wait_until(wait_until)
    custom_wait_ms = _parse_wait_ms(wait_until)

    page = None
    try:
        page = await browser.new_page()

        try:
            response = await page.goto(
                url,
                wait_until=wait_strategy or "load",
                timeout=DEFAULT_TIMEOUT_MS,
            )

            if response is not None and response.status in (403, 406):
                raise BotBlockedError(f"Target site returned {response.status}")

            if custom_wait_ms:
                await page.wait_for_timeout(custom_wait_ms)
            elif not wait_strategy:
                await page.wait_for_timeout(EXTRA_WAIT_MS)
                await _wait_for_content(page)

        except (BotBlockedError, InvalidUrlError):
            raise
        except asyncio.TimeoutError:
            raise FetchTimeoutError(f"Timeout fetching {url}")
        except Exception as e:
            raise FetchFailedError(f"Navigation failed: {e}")

        try:
            content = await page.content()
        except Exception as e:
            raise FetchFailedError(f"Failed to get page content: {e}")

        return content
    finally:
        if page:
            await page.close()


def _resolve_wait_until(value: str | None) -> str | None:
    if not value:
        return None
    if value in VALID_WAIT_UNTIL:
        return value
    return None


def _parse_wait_ms(value: str | None) -> int | None:
    if not value:
        return None
    if value.isdigit():
        return int(value)
    return None


class BotBlockedError(Exception):
    pass


class FetchTimeoutError(Exception):
    pass


class FetchFailedError(Exception):
    pass


class InvalidUrlError(Exception):
    pass


def semaphore() -> asyncio.Semaphore:
    return _semaphore


async def shutdown():
    async with _browsers_lock:
        for seed, browser in list(_browsers.items()):
            await browser.close()
            logger.info("CloakBrowser closed (fingerprint=%s)", seed)
        _browsers.clear()


def active_count() -> int:
    return MAX_CONCURRENT - _semaphore._value
