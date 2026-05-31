import logging
import traceback
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.responses import JSONResponse, PlainTextResponse
from pydantic import BaseModel

from browser_manager import (
    BotBlockedError,
    FetchFailedError,
    FetchTimeoutError,
    InvalidUrlError,
    active_count,
    fetch_html,
    get_browser,
    semaphore,
    shutdown,
)

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
logger = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI):
    logger.info("Starting cloak-service...")
    await get_browser("0")
    yield
    await shutdown()


app = FastAPI(title="cloak-service", version="0.1.0", lifespan=lifespan)


class FetchRequest(BaseModel):
    url: str
    wait_until: str | None = None
    proxy: str | None = None


@app.post("/fetch")
async def fetch_endpoint(req: FetchRequest):
    async with semaphore():
        try:
            html = await fetch_html(req.url, req.wait_until, req.proxy)
            return PlainTextResponse(content=html, media_type="text/html; charset=utf-8")
        except InvalidUrlError as e:
            return JSONResponse(status_code=502, content={"code": "INVALID_URL", "error": str(e)})
        except BotBlockedError as e:
            return JSONResponse(status_code=502, content={"code": "BOT_BLOCKED", "error": str(e)})
        except FetchTimeoutError as e:
            return JSONResponse(status_code=502, content={"code": "FETCH_TIMEOUT", "error": str(e)})
        except FetchFailedError as e:
            return JSONResponse(status_code=502, content={"code": "FETCH_FAILED", "error": str(e)})
        except Exception:
            logger.exception("Unexpected error fetching %s", req.url)
            return JSONResponse(status_code=502, content={"code": "FETCH_FAILED", "error": traceback.format_exc()})


@app.get("/health")
async def health():
    return {
        "status": "ok",
        "active_pages": active_count(),
        "max_pages": semaphore()._value + active_count(),
    }
