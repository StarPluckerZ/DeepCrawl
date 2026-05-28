import asyncio
import logging
import os
from urllib.parse import urlparse

from cloakbrowser import launch_async

logger = logging.getLogger(__name__)

MAX_CONCURRENT = int(os.getenv("CLOAKBROWSER_MAX_CONCURRENT", "3"))
DEFAULT_TIMEOUT_MS = int(os.getenv("CLOAKBROWSER_TIMEOUT_MS", "30000"))

VALID_WAIT_UNTIL = {"load", "networkidle", "domcontentloaded", "commit"}

_browser = None
_semaphore = asyncio.Semaphore(MAX_CONCURRENT)
_browser_lock = asyncio.Lock()


def _is_http_url(url: str) -> bool:
    try:
        parsed = urlparse(url)
        return parsed.scheme in ("http", "https") and bool(parsed.netloc)
    except Exception:
        return False


async def get_browser():
    global _browser
    async with _browser_lock:
        if _browser is None:
            _browser = await launch_async(humanize=True)
            logger.info("CloakBrowser launched (max_concurrent=%d)", MAX_CONCURRENT)
    return _browser


async def fetch_html(url: str, wait_until: str | None = None, proxy: str | None = None) -> str:
    if not _is_http_url(url):
        raise InvalidUrlError(f"Invalid URL: {url}")

    browser = await get_browser()
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
    global _browser
    if _browser:
        await _browser.close()
        _browser = None
        logger.info("CloakBrowser closed")


def active_count() -> int:
    return MAX_CONCURRENT - _semaphore._value
