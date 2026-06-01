namespace DeepCrawl.Infrastructure.Caching;

public class RedisOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6379;
    public string Password { get; set; } = string.Empty;
    public int Database { get; set; } = 0;
    public string Prefix { get; set; } = "deepcrawl";
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(30);
}
