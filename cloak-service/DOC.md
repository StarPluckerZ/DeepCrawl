# cloak-service — FastAPI + CloakBrowser 反爬网页获取服务

---

## 架构

```
main.py          → FastAPI 应用 + /fetch /health 端点
browser_manager.py → 浏览器生命周期管理、并发控制、代理隔离
```

## 端点

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/fetch` | 获取 URL 原始 HTML |
| GET  | `/health` | 服务健康检查 |

## 请求格式

```json
POST /fetch
{
    "url": "https://example.com",
    "wait_until": "networkidle",   // 可选, 默认 "load"
    "proxy": null                   // 可选, 如 "http://user:pass@host:8080"
}
```

## 响应

- 200: `text/html` 原始内容
- 502: `application/json` `{ "error": "...", "code": "BOT_BLOCKED|FETCH_TIMEOUT|FETCH_FAILED|INVALID_URL" }`

## 错误码

| code | 含义 |
|------|------|
| BOT_BLOCKED | 目标站点返回 403/验证码 |
| FETCH_TIMEOUT | 页面加载超时 |
| FETCH_FAILED | 网络/浏览器错误 |
| INVALID_URL | URL 格式不合法 |

## 浏览器管理

- 启动时创建全局 browser 实例（单例复用）
- asyncio.Semaphore(3) 控制最大并发页面数
- 有 proxy → 创建独立 BrowserContext → 用完即销毁
- 无 proxy → 复用默认 context，仅新建 page
- page.goto() 30s 超时
