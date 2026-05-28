<p align="center">
  <a href="README.md">English</a>
</p>

# DeepCrawl

**面向 AI 工作流的网页内容提取服务 — 反爬抓取 + AI 清洗 → 干净的结构化 Markdown。**

DeepCrawl 通过反爬引擎（CloakBrowser）获取任意网页，经规则清洗去除噪声，转为 Markdown，再可选接入大模型精修。为需要干净、LLM-ready 数据的 AI 工作流设计。

## 功能特性

- **反爬穿透** — CloakBrowser（底层 Chromium 源码级补丁）通过 Cloudflare Turnstile、reCAPTCHA v3 等 30+ 项检测
- **Firecrawl 兼容 API** — `POST /v2/scrape` 端点，响应格式与 Firecrawl 一致，可直接替换
- **可插拔清洗管线** — HTML/Markdown 两阶段，按 Order 排序执行，新增清洗步骤无需改核心逻辑
- **大模型二次清洗** — 接入 OpenAI 兼容 API，语义级去除残留噪声（可选）
- **元数据提取** — OpenGraph、标题、描述、语言、状态码等
- **智能缓存** — URL + HTML 哈希 + 上下文感知，避免重复 LLM 调用
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

首次启动时，API 会在控制台打印一个随机生成的 token。查看方式：

```bash
docker compose logs deepcrawl-api | head -20
# 或本地运行时直接看控制台输出：
dotnet run --project src/DeepCrawl.Api
```

注意类似下方的 banner：

```
╔══════════════════════════════════════════════════════════╗
║         DEEPCRAWL API TOKEN                              ║
║           sk-xxxxxxxx...                                  ║
╚══════════════════════════════════════════════════════════╝
```

> Token **仅打印一次**，请务必保存。

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
  → CloakBrowser（Python 反爬引擎）
    → CleanPipeline 清洗管线
      [Html/0]  元数据提取
      [Html/10] AngleSharp 标签清洗
      [Html/20] 剥离 base64 data URI
      [Md/10]   ReverseMarkdown 转换
      [Md/20]   LLM 精修（可选）
    → PostgreSQL 缓存
  → Firecrawl 兼容 JSON
```

基于 .NET 10，DDD 分层架构：

```
src/
├── DeepCrawl.Api/             ← Web API 宿主
├── DeepCrawl.Core/            ← 应用层
├── DeepCrawl.Domain/          ← 领域实体与接口
└── DeepCrawl.Infrastructure/  ← 外部依赖实现

cloak-service/                 ← Python 反爬服务
```

## 配置

| 变量 | 必填 | 默认值 | 说明 |
|------|------|--------|------|
| `POSTGRES_PASSWORD` | 是 | — | PostgreSQL 密码 |
| `AI_BASEURL` | 是 | — | OpenAI 兼容 API 地址 |
| `AI_APIKEY` | 是 | — | API 密钥 |
| `AI_MODEL` | 是 | — | 模型名称（如 `Qwen/Qwen3-8B`） |
| `AI__ThinkingLevel` | 否 | — | 深度思考级别：`"low"`、`"medium"`、`"high"`、`"none"` |

## License

MIT
