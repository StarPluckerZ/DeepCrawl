<p align="center">
  <a href="README_CN.md">中文文档</a>
</p>

# DeepCrawl

**Web content extraction service — anti-bot crawling + AI-powered cleaning → clean, structured Markdown.**

DeepCrawl fetches any web page through anti-bot countermeasures (CloakBrowser), strips noise with rule-based HTML cleaning, converts to Markdown, then optionally polishes with LLM. Built for AI workflows that need clean, LLM-ready data.

## Features

- **Anti-bot bypass** — CloakBrowser (patched Chromium) passes Cloudflare Turnstile, reCAPTCHA v3, and 30+ bot detection tests
- **Firecrawl-compatible API** — drop-in replacement for `POST /v2/scrape` with identical response format
- **Pluggable cleaning pipeline** — HTML/Markdown stages, add custom steps without touching core logic
- **LLM post-cleaning** — OpenAI-compatible API for semantic noise removal (optional)
- **Metadata extraction** — OpenGraph, title, description, language, status code, etc.
- **Smart caching** — URL + HTML hash + context-aware; avoids redundant LLM calls
- **Docker Compose** — one-command startup for all services

## Quick Start

### Prerequisites

- [Docker](https://docs.docker.com/get-docker/)
- .NET 10 SDK (for local development only)

### 1. Clone & configure

```bash
git clone https://github.com/your-org/DeepCrawl.git
cd DeepCrawl
cp .env.example .env
```

Edit `.env` and set your AI provider credentials:

```env
POSTGRES_PASSWORD=your-password
AI_BASEURL=https://api.siliconflow.cn/v1/chat/completions
AI_APIKEY=sk-your-key-here
AI_MODEL=Qwen/Qwen3-8B
```

### 2. Start infrastructure

```bash
docker compose up -d
```

### 3. Get your API token

On first launch, the API prints a token to the console logs. Check with:

```bash
docker compose logs deepcrawl-api | head -20
# OR if running locally:
dotnet run --project src/DeepCrawl.Api
```

Look for the banner:

```
╔══════════════════════════════════════════════════════════╗
║         DEEPCRAWL API TOKEN                              ║
║           sk-xxxxxxxx...                                  ║
╚══════════════════════════════════════════════════════════╝
```

> The token is printed **once**. Save it. It will not be shown again.

### 4. Start full stack (optional)

To run the API inside Docker as well:

```bash
docker compose --profile prod up -d
```

### 5. Test

```bash
curl -s -X POST http://localhost:5266/v2/scrape \
  -H "Authorization: Bearer sk-your-token" \
  -H "Content-Type: application/json" \
  -d '{"url":"https://example.com","formats":["markdown"]}'
```

## API Reference

### `POST /v2/scrape`

Firecrawl-compatible endpoint.

**Request:**

```json
{
  "url": "https://example.com",
  "formats": ["markdown", "html"],
  "waitUntil": "networkidle",
  "proxy": "http://user:pass@host:8080"
}
```

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `url` | string | *required* | Target URL to scrape |
| `formats` | string[] | `["markdown"]` | Output formats: `"markdown"` and/or `"html"` |
| `waitUntil` | string | `"load"` | Page load strategy: `"load"`, `"networkidle"`, `"domcontentloaded"`, or milliseconds `"3000"` |
| `proxy` | string | null | HTTP/SOCKS5 proxy for the request |

**Response (200):**

```json
{
  "success": true,
  "data": {
    "markdown": "# Title\n\nContent...",
    "html": "<div>Cleaned HTML...</div>",
    "metadata": {
      "title": "Page Title",
      "description": "Page description",
      "language": "en",
      "sourceURL": "https://example.com",
      "statusCode": 200,
      "contentType": "text/html"
    }
  }
}
```

**Response (401):**

```json
{
  "success": false,
  "error": "Unauthorized: Invalid or missing API token."
}
```

### Other Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/crawl/{id}` | Get historical crawl record |
| `GET` | `/content?url=...` | Get cached result (no re-crawl) |
| `GET` | `/health` | Health check |

## Architecture

```
Request → Token Auth
  → CloakBrowser (Python, anti-bot)
    → CleanPipeline
      [Html/0]  Metadata extraction
      [Html/10] AngleSharp tag removal
      [Html/20] Strip base64 data URIs
      [Md/10]   ReverseMarkdown
      [Md/20]   LLM cleaning (optional)
    → PostgreSQL cache
  → Firecrawl-compatible JSON
```

Built with .NET 10, following DDD layered architecture:

```
src/
├── DeepCrawl.Api/             ← Web API host
├── DeepCrawl.Core/            ← Application layer
├── DeepCrawl.Domain/          ← Domain entities & interfaces
└── DeepCrawl.Infrastructure/  ← External dependencies

cloak-service/                 ← Python anti-bot service
```

## Configuration

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `POSTGRES_PASSWORD` | Yes | — | PostgreSQL password |
| `AI_BASEURL` | Yes | — | OpenAI-compatible API base URL |
| `AI_APIKEY` | Yes | — | API key |
| `AI_MODEL` | Yes | — | Model name (e.g. `Qwen/Qwen3-8B`) |
| `AI__ThinkingLevel` | No | — | Deep reasoning: `"low"`, `"medium"`, `"high"`, `"none"` |

## License

MIT
