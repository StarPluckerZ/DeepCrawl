namespace DeepCrawl.Domain.Abstractions;

public interface ITokenValidator
{
    Task<bool> ValidateAsync(string? token, CancellationToken ct = default);
}
