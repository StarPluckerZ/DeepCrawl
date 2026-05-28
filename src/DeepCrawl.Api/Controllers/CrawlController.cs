using DeepCrawl.Core.Dtos;
using DeepCrawl.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace DeepCrawl.Api.Controllers;

[ApiController]
public class CrawlController : ControllerBase
{
    private readonly CrawlPipeline _pipeline;

    public CrawlController(CrawlPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    [HttpPost("/v2/scrape")]
    public async Task<IActionResult> Scrape([FromBody] ScrapeRequest request, CancellationToken ct)
    {
        var token = ExtractToken();
        var result = await _pipeline.ScrapeAsync(request, token, ct);

        if (!result.Success)
            return Unauthorized(result);

        return Ok(result);
    }

    [HttpGet("/crawl/{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        var result = await _pipeline.GetByIdAsync(id, ct);
        return result is not null ? Ok(result) : NotFound();
    }

    [HttpGet("/content")]
    public async Task<IActionResult> GetContent([FromQuery] string url, CancellationToken ct)
    {
        var result = await _pipeline.GetContentAsync(url, ct);
        return result is not null ? Ok(result) : NotFound();
    }

    [HttpGet("/health")]
    public IActionResult Health()
    {
        return Ok(new { status = "ok" });
    }

    private string? ExtractToken()
    {
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(auth)) return null;
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? auth["Bearer ".Length..].Trim()
            : auth;
    }
}
