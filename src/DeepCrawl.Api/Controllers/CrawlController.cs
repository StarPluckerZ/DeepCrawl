using DeepCrawl.Core.Dtos;
using DeepCrawl.Core.Services;
using Microsoft.AspNetCore.Authorization;
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
    [Authorize]
    public async Task<IActionResult> Scrape([FromBody] ScrapeRequest request, CancellationToken ct)
    {
        var result = await _pipeline.ScrapeAsync(request, ct);

        if (!result.Success)
            return Unauthorized(result);

        return Ok(result);
    }

    [HttpGet("/health")]
    public IActionResult Health()
    {
        return Ok(new { status = "ok" });
    }
}
