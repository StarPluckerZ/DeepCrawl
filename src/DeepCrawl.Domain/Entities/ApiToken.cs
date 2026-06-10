using FreeSql.DataAnnotations;

namespace DeepCrawl.Domain.Entities;

[Table(Name = "api_tokens")]
public class ApiToken
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    [Column(DbType = "varchar(128)", IsNullable = false)]
    public string Token { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
