namespace UrlShortener.Domain.Entities;

public class ShortUrl
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OriginalUrl { get; set; }
    public required string ShortCode { get; set; }
    public string? CustomAlias { get; set; }
    public long ClickCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
}
