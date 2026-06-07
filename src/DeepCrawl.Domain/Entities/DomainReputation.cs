using FreeSql.DataAnnotations;

namespace DeepCrawl.Domain.Entities;

[Table(Name = "domain_reputations")]
[Index("uk_domain_reputations_domain", "Domain", IsUnique = true)]
public class DomainReputation
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    [Column(StringLength = 253, IsNullable = false)]
    public string Domain { get; set; } = null!;

    public int ConsecutiveFailures { get; set; }
    public int TotalFailures { get; set; }
    public DateTime? BlockedUntil { get; set; }
    public DateTime LastFailureAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }

    [Column(CanUpdate = false, ServerTime = DateTimeKind.Local)]
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
