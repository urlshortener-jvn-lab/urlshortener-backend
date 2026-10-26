using UrlShortener.Application.DTOs;

namespace UrlShortener.Application.Interfaces;

public interface IShortUrlService
{
    Task<ShortUrlDto> CreateAsync(CreateShortUrlRequest request, string baseUrl, CancellationToken ct = default);
    Task<IReadOnlyList<ShortUrlDto>> GetRecentAsync(string baseUrl, int limit = 20, CancellationToken ct = default);
    Task<string?> ResolveAndTrackClickAsync(string code, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<SystemStatsDto> GetSystemStatsAsync(CancellationToken ct = default);
}
