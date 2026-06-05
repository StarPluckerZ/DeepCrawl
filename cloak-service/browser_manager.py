import asyncio
import hashlib
import logging
import os
from collections import OrderedDict
from urllib.parse import urlparse

from cloakbrowser import launch_async

logger = logging.getLogger(__name__)

MAX_CONCURRENT = int(os.getenv("CLOAKBROWSER_MAX_CONCURRENT", "3"))
DEFAULT_TIMEOUT_MS = int(os.getenv("CLOAKBROWSER_TIMEOUT_MS", "30000"))
MAX_BROWSERS = int(os.getenv("CLOAKBROWSER_MAX_BROWSERS", str(MAX_CONCURRENT)))
EXTRA_WAIT_MS = int(os.getenv("CLOAKBROWSER_EXTRA_WAIT_MS", "2000"))

VALID_WAIT_UNTIL = {"load", "networkidle", "domcontentloaded", "commit"}

_browsers: OrderedDict[str, object] = OrderedDict()
_semaphore = asyncio.Semaphore(MAX_CONCURRENT)
_browsers_lock = asyncio.Lock()


def _compute_seed(domain: str) -> str:
    h = hashlib.sha256(domain.encode()).hexdigest()[:8]
    return str(int(h, 16) % 100_000)


def _extract_domain(url: str) -> str:
    return urlparse(url).hostname or url


_UNNECESSARY_TYPES = ("image", "media", "font", "websocket", "eventsource", "ping")


def _block_unnecessary_resources(route):
    if route.request.resource_type in _UNNECESSARY_TYPES:
        route.abort()
    else:
        route.continue_()


def _is_http_url(url: str) -> bool:
    try:
        parsed = urlparse(url)
        return parsed.scheme in ("http", "https") and bool(parsed.netloc)
    except Exception:
        return False


async def get_browser(seed: str):
    async with _browsers_lock:
        if seed in _browsers:
            _browsers.move_to_end(seed)
            return _browsers[seed]

        while len(_browsers) >= MAX_BROWSERS:
            evict_seed, evict_browser = _browsers.popitem(last=False)
            await evict_browser.close()
            logger.info("Evicted browser for fingerprint seed %s (max_browsers=%d)", evict_seed, MAX_BROWSERS)

        browser = await launch_async(
            humanize=True,
            args=[f"--fingerprint={seed}"],
        )
        _browsers[seed] = browser
        logger.info("CloakBrowser launched (fingerprint=%s, active_browsers=%d)", seed, len(_browsers))
        return browser


async def fetch_html(url: str, wait_until: str | None = None, proxy: str | None = None) -> str:
    if not _is_http_url(url):
        raise InvalidUrlError(f"Invalid URL: {url}")

    domain = _extract_domain(url)
    seed = _compute_seed(domain)
    browser = await get_browser(seed)

    wait_strategy = _resolve_wait_until(wait_until)
    custom_wait_ms = _parse_wait_ms(wait_until)

    context = None
    page = None
    try:
        if proxy:
            context = await browser.new_context(proxy={"server": proxy})
            page = await context.new_page()
        else:
            page = await browser.new_page()

        await page.route("**/*", _block_unnecessary_resources)

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
        if context:
            await context.close()


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
