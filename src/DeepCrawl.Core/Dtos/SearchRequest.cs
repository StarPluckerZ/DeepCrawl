using System.ComponentModel.DataAnnotations;

namespace DeepCrawl.Core.Dtos;

public record SearchRequest
{
    [Required]
    [StringLength(500)]
    public string Query { get; init; } = null!;

    [Range(1, 100)]
    public int? Limit { get; init; } = 10;

    public List<string>? Sources { get; init; }

    public string? Tbs { get; init; }

    public string? Location { get; init; }

    public string? Country { get; init; }

    public List<string>? IncludeDomains { get; init; }

    public List<string>? ExcludeDomains { get; init; }

    [Range(1000, 300000)]
    public int? Timeout { get; init; } = 60000;
}
