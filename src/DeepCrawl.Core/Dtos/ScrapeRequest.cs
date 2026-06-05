namespace DeepCrawl.Core.Dtos;

public record ScrapeRequest(string Url, List<string>? Formats = null, string? WaitUntil = null);
