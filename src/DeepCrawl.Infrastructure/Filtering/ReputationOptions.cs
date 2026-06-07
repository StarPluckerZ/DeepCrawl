namespace DeepCrawl.Infrastructure.Filtering;

public class ReputationOptions
{
    public bool Enabled { get; set; } = true;
    public int BaseBlockMinutes { get; set; } = 5;
    public int BlockThreshold { get; set; } = 3;
    public int MaxBlockMinutes { get; set; } = 10080;
}
