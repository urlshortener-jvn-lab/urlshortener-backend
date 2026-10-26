namespace UrlShortener.Application.DTOs;

public record CreateShortUrlRequest(
    string OriginalUrl,
    string? CustomAlias = null,
    int? ExpiresInDays = null
);

public record ShortUrlDto(
    Guid Id,
    string OriginalUrl,
    string ShortCode,
    string ShortLink,
    long ClickCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    bool IsActive,
    string ServedByInstance
);

public record SystemStatsDto(
    int TotalUrls,
    long TotalClicks,
    string InstanceName,
    string Environment,
    bool RedisConnected,
    bool DatabaseConnected
);
