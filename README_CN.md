<p align="center">
  <a href="README.md">English</a>
</p>

# DeepCrawl

**面向 AI 工作流的网页内容提取服务 — 反爬抓取 + AI 清洗 → 干净的结构化 Markdown。**

DeepCrawl 通过反爬引擎（CloakBrowser）获取任意网页，经规则清洗去除噪声，转为 Markdown，再可选接入大模型精修。为需要干净、LLM-ready 数据的 AI 工作流设计。

## 功能特性

- **分级抓取** — 五层级联、成本从低到高：智谱 Web Reader（markdown）→ HttpClient → HttpClient+代理 → CloakBrowser → CloakBrowser+代理，失败或内容过薄时自动降级
- **反爬穿透** — CloakBrowser（底层 Chromium 源码级补丁）通过 Cloudflare Turnstile、reCAPTCHA v3 等 30+ 项检测
- **Firecrawl 兼容 API** — `POST /v2/scrape`、`POST /v2/search` 端点，响应格式与 Firecrawl 一致，可直接替换
- **清洗管线** — HTML/Markdown 两阶段，内置 qRead 段落密度正文抽取（针对中文页面调优），可选接入大模型精修（任意 OpenAI 兼容 API）
- **网页搜索** — 智谱（默认）或博查，Firecrawl 兼容端点，叠加 uBlacklist 内容农场黑名单与动态域名信誉过滤
- **元数据提取** — OpenGraph、标题、描述、语言、状态码等
- **智能缓存** — URL + HTML 哈希 + 上下文感知，避免重复 LLM 调用
- **命令行工具** — 数据库 Schema 同步、API Token 管理
- **Docker Compose 一键启动** — 全栈编排

## 快速开始

### 环境要求

- [Docker](https://docs.docker.com/get-docker/)
- .NET 10 SDK（仅本地开发需要）

### 1. 克隆并配置

```bash
git clone https://github.com/your-org/DeepCrawl.git
cd DeepCrawl
cp .env.example .env
```

编辑 `.env`，填入 AI 服务凭据：

```env
POSTGRES_PASSWORD=your-password
AI_BASEURL=https://api.siliconflow.cn/v1/chat/completions
AI_APIKEY=sk-your-key-here
AI_MODEL=Qwen/Qwen3-8B
```

### 2. 启动基础设施

```bash
docker compose up -d
```

### 3. 获取 API Token

Token 通过自带的命令行工具创建（命令同时会同步数据库 Schema，可直接对全新数据库运行）：

```bash
# 一次性初始化：Schema 同步 + 首个 token（幂等，可重复执行）
dotnet run --project src/DeepCrawl.CommandLineTools -- setup

# 或在 compose 网络内运行
docker compose --profile cli run --rm deepcrawl-cli setup
```

输出示例：

```
[synced] Database schema (crawl_records, crawl_statistics, api_tokens, domain_reputations)
──────────────────────────────────────────────
  Token   : default (id=1)
  API Key (save this — shown once):
  sk-xxxxxxxx...
──────────────────────────────────────────────
```

> Token **仅打印一次**，请务必保存。后续管理 token：

```bash
dotnet run --project src/DeepCrawl.CommandLineTools -- token create --name laptop
dotnet run --project src/DeepCrawl.CommandLineTools -- token list
dotnet run --project src/DeepCrawl.CommandLineTools -- token revoke --id 2
```

### 4. 启动全栈（可选）

如需将 API 也放入 Docker 运行：

```bash
docker compose --profile prod up -d
```

### 5. 测试

```bash
curl -s -X POST http://localhost:5266/v2/scrape \
  -H "Authorization: Bearer sk-your-token" \
  -H "Content-Type: application/json" \
  -d '{"url":"https://example.com","formats":["markdown"]}'
```

## API 参考

### `POST /v2/scrape`

Firecrawl 兼容端点。

**请求：**

```json
{
  "url": "https://example.com",
  "formats": ["markdown", "html"],
  "waitUntil": "networkidle",
  "proxy": "http://user:pass@host:8080"
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `url` | string | *必填* | 目标 URL |
| `formats` | string[] | `["markdown"]` | 输出格式：`"markdown"` 和/或 `"html"` |
| `waitUntil` | string | `"load"` | 页面加载策略：`"load"`、`"networkidle"`、`"domcontentloaded"`，或毫秒数 `"3000"` |
| `proxy` | string | null | HTTP/SOCKS5 代理地址 |

**响应（200）：**

```json
{
  "success": true,
  "data": {
    "markdown": "# 标题\n\n正文内容...",
    "html": "<div>清洗后 HTML...</div>",
    "metadata": {
      "title": "页面标题",
      "description": "页面描述",
      "language": "zh",
      "sourceURL": "https://example.com",
      "statusCode": 200,
      "contentType": "text/html"
    }
  }
}
```

**响应（401）：**

```json
{
  "success": false,
  "error": "Unauthorized: Invalid or missing API token."
}
```

### 其他端点

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/crawl/{id}` | 查询历史爬取记录 |
| `GET` | `/content?url=...` | 获取缓存结果（不触发爬取） |
| `GET` | `/health` | 健康检查 |

## 架构

```
请求 → Token 鉴权
  → TieredHttpFetcher 分级抓取（失败/内容过薄时逐级降级）
      Tier 0  智谱 Web Reader（markdown，配置 ZHIPU_APIKEY 后启用）
      Tier 1  HttpClient 直连
      Tier 2  HttpClient + 代理
      Tier 3  CloakBrowser（Python 反爬引擎）
      Tier 4  CloakBrowser + 代理
    → CleanPipeline 清洗管线
      [Html/0]  元数据提取
      [Html/10] AngleSharp 标签清洗
      [Html/15] qRead 正文抽取
      [Html/20] 剥离 base64 data URI
      [Md/10]   ReverseMarkdown 转换
      [Md/15]   空白规范化
      [Md/20]   LLM 精修（可选）
    → PostgreSQL + Redis 缓存
  → Firecrawl 兼容 JSON
```

基于 .NET 10，DDD 分层架构：

```
src/
├── DeepCrawl.Api/              ← Web API 宿主
├── DeepCrawl.Core/             ← 应用层
├── DeepCrawl.Domain/           ← 领域实体与接口
├── DeepCrawl.Infrastructure/   ← 外部依赖实现
└── DeepCrawl.CommandLineTools/ ← 数据库初始化与 Token 管理 CLI

cloak-service/                  ← Python 反爬服务
```

## 配置

| 变量 | 必填 | 默认值 | 说明 |
|------|------|--------|------|
| `POSTGRES_PASSWORD` | 是 | — | PostgreSQL 密码 |
| `AI_BASEURL` | 是 | — | OpenAI 兼容 API 地址 |
| `AI_APIKEY` | 是 | — | API 密钥 |
| `AI_MODEL` | 是 | — | 模型名称（如 `Qwen/Qwen3-8B`） |
| `AI__ThinkingLevel` | 否 | — | 深度思考级别：`"low"`、`"medium"`、`"high"`、`"none"` |
| `ZHIPU_APIKEY` | 否 | — | 智谱密钥；启用 Reader 抓取层与智谱搜索 |
| `SEARCH_PROVIDER` | 否 | `Zhipu` | 搜索引擎：`Zhipu` 或 `Bocha` |

## License

MIT
