<p align="center">
  <a href="README_CN.md">中文文档</a>
</p>

# DeepCrawl

**Web content extraction service — anti-bot crawling + AI-powered cleaning → clean, structured Markdown.**

DeepCrawl fetches any web page through anti-bot countermeasures (CloakBrowser), strips noise with rule-based HTML cleaning, converts to Markdown, then optionally polishes with LLM. Built for AI workflows that need clean, LLM-ready data.

## Features

- **Tiered fetching** — five-level cascade, cheapest first: Zhipu web reader (markdown) → HttpClient → HttpClient+proxy → CloakBrowser → CloakBrowser+proxy; each tier falls back automatically on failure or thin content
- **Anti-bot bypass** — CloakBrowser (patched Chromium) passes Cloudflare Turnstile, reCAPTCHA v3, and 30+ bot detection tests
- **Firecrawl-compatible API** — drop-in replacement for `POST /v2/scrape` and `POST /v2/search` with identical response format
- **Cleaning pipeline** — HTML/Markdown stages with qRead paragraph-density main-content extraction (tuned for Chinese pages), plus optional LLM post-cleaning via any OpenAI-compatible API
- **Web search** — Zhipu (default) or Bocha behind a Firecrawl-compatible endpoint, filtered by uBlacklist blocklists and a dynamic domain-reputation system
- **Metadata extraction** — OpenGraph, title, description, language, status code, etc.
- **Smart caching** — URL + HTML hash + context-aware; avoids redundant LLM calls
- **Command-line tools** — database schema sync, API token management
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

Tokens are created with the bundled command-line tools (they also sync the database schema, so they are safe to run against a fresh database):

```bash
# one-shot: schema sync + initial token (idempotent)
dotnet run --project src/DeepCrawl.CommandLineTools -- setup

# or inside the compose network
docker compose --profile cli run --rm deepcrawl-cli setup
```

Output:

```
[synced] Database schema (crawl_records, crawl_statistics, api_tokens, domain_reputations)
──────────────────────────────────────────────
  Token   : default (id=1)
  API Key (save this — shown once):
  sk-xxxxxxxx...
──────────────────────────────────────────────
```

> The token is printed **once**. Save it. To add or manage tokens later:

```bash
dotnet run --project src/DeepCrawl.CommandLineTools -- token create --name laptop
dotnet run --project src/DeepCrawl.CommandLineTools -- token list
dotnet run --project src/DeepCrawl.CommandLineTools -- token revoke --id 2
```

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
  → TieredHttpFetcher (fall through on failure / thin content)
      Tier 0  Zhipu web reader (markdown, if ZHIPU_APIKEY set)
      Tier 1  HttpClient direct
      Tier 2  HttpClient + proxy
      Tier 3  CloakBrowser (Python, anti-bot)
      Tier 4  CloakBrowser + proxy
    → CleanPipeline
      [Html/0]  Metadata extraction
      [Html/10] AngleSharp tag removal
      [Html/15] qRead main-content extraction
      [Html/20] Strip base64 data URIs
      [Md/10]   ReverseMarkdown
      [Md/15]   Whitespace normalization
      [Md/20]   LLM cleaning (optional)
    → PostgreSQL + Redis cache
  → Firecrawl-compatible JSON
```

Built with .NET 10, following DDD layered architecture:

```
src/
├── DeepCrawl.Api/              ← Web API host
├── DeepCrawl.Core/             ← Application layer
├── DeepCrawl.Domain/           ← Domain entities & interfaces
├── DeepCrawl.Infrastructure/   ← External dependencies
└── DeepCrawl.CommandLineTools/ ← DB init & token management CLI

cloak-service/                  ← Python anti-bot service
```

## Configuration

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `POSTGRES_PASSWORD` | Yes | — | PostgreSQL password |
| `AI_BASEURL` | Yes | — | OpenAI-compatible API base URL |
| `AI_APIKEY` | Yes | — | API key |
| `AI_MODEL` | Yes | — | Model name (e.g. `Qwen/Qwen3-8B`) |
| `AI__ThinkingLevel` | No | — | Deep reasoning: `"low"`, `"medium"`, `"high"`, `"none"` |
| `ZHIPU_APIKEY` | No | — | Zhipu key; enables the reader fetch tier and Zhipu search |
| `SEARCH_PROVIDER` | No | `Zhipu` | Search engine: `Zhipu` or `Bocha` |

## License

MIT
