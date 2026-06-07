using DeepCrawl.Core.Dtos;
using DeepCrawl.Core.Services;
using DeepCrawl.Domain.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeepCrawl.Api.Controllers;

[ApiController]
public class SearchController : ControllerBase
{
    private readonly ISearchService _service;
    private readonly ISearchProvider _searchProvider;
    private readonly IRedisClient _redis;

    public SearchController(ISearchService service, ISearchProvider searchProvider, IRedisClient redis)
    {
        _service = service;
        _searchProvider = searchProvider;
        _redis = redis;
    }

    [HttpPost("/v2/search")]
    [Authorize]
    public async Task<IActionResult> Search([FromBody] SearchRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new SearchResponse { Success = false, Error = "Invalid request" });

        var result = await _service.SearchAsync(request, ct);

        if (!result.Success)
            return StatusCode(502, result);

        return Ok(result);
    }

    [HttpGet("/v2/search/health")]
    public async Task<IActionResult> Health()
    {
        var components = new Dictionary<string, string>();
        try
        {
            await _redis.SetAsync(new("Health", "search-check", TimeSpan.FromSeconds(5)), "ok");
            components["redis"] = "ok";
        }
        catch
        {
            components["redis"] = "unavailable";
        }

        components["provider"] = _searchProvider.ProviderName;

        return Ok(new { status = "ok", components });
    }
}
