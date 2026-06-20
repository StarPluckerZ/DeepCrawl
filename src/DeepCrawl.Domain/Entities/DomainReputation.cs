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

    [Column(DbType = "timestamptz")]
    public DateTime? BlockedUntil { get; set; }

    [Column(DbType = "timestamptz")]
    public DateTime LastFailureAt { get; set; }

    [Column(DbType = "timestamptz")]
    public DateTime? LastSuccessAt { get; set; }

    [Column(CanUpdate = false, ServerTime = DateTimeKind.Utc, DbType = "timestamptz")]
    public DateTime CreatedAt { get; set; }

    [Column(DbType = "timestamptz")]
    public DateTime UpdatedAt { get; set; }
}
