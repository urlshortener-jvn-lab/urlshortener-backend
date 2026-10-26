using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UrlShortener.Application.DTOs;
using UrlShortener.Application.Interfaces;
using UrlShortener.Domain.Entities;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Services;

public class ShortUrlService(
    AppDbContext dbContext,
    IDistributedCache cache,
    IHostEnvironment environment,
    ILogger<ShortUrlService> logger
) : IShortUrlService
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly string InstanceName = Environment.MachineName;

    public async Task<ShortUrlDto> CreateAsync(
        CreateShortUrlRequest request,
        string baseUrl,
        CancellationToken ct = default
    )
    {
        if (!Uri.TryCreate(request.OriginalUrl, UriKind.Absolute, out var parsedUri)
            || (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("URL gốc không hợp lệ (cần bắt đầu bằng http:// hoặc https://).");
        }

        string code;
        if (!string.IsNullOrWhiteSpace(request.CustomAlias))
        {
            code = request.CustomAlias.Trim();
            var exists = await dbContext.ShortUrls.AnyAsync(x => x.ShortCode == code, ct);
            if (exists)
            {
                throw new InvalidOperationException($"Alias '{code}' đã tồn tại, vui lòng chọn mã khác.");
            }
        }
        else
        {
            code = await GenerateUniqueCodeAsync(ct);
        }

        var entity = new ShortUrl
        {
            OriginalUrl = request.OriginalUrl.Trim(),
            ShortCode = code,
            CustomAlias = request.CustomAlias?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = request.ExpiresInDays.HasValue
                ? DateTimeOffset.UtcNow.AddDays(request.ExpiresInDays.Value)
                : null,
            IsActive = true
        };

        dbContext.ShortUrls.Add(entity);
        await dbContext.SaveChangesAsync(ct);

        await CacheUrlAsync(entity.ShortCode, entity.OriginalUrl, ct);

        return MapToDto(entity, baseUrl);
    }

    public async Task<IReadOnlyList<ShortUrlDto>> GetRecentAsync(
        string baseUrl,
        int limit = 20,
        CancellationToken ct = default
    )
    {
        var items = await dbContext.ShortUrls
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return items.Select(x => MapToDto(x, baseUrl)).ToList();
    }

    public async Task<string?> ResolveAndTrackClickAsync(string code, CancellationToken ct = default)
    {
        var cacheKey = $"shorturl:{code}";
        string? cachedUrl = null;

        try
        {
            cachedUrl = await cache.GetStringAsync(cacheKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không đọc được Redis cache cho mã {Code}, fallback xuống DB.", code);
        }

        if (!string.IsNullOrEmpty(cachedUrl))
        {
            await dbContext.ShortUrls
                .Where(x => x.ShortCode == code)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ClickCount, x => x.ClickCount + 1), ct);
            return cachedUrl;
        }

        var entity = await dbContext.ShortUrls
            .FirstOrDefaultAsync(x => x.ShortCode == code && x.IsActive, ct);

        if (entity is null)
        {
            return null;
        }

        if (entity.ExpiresAt.HasValue && entity.ExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            return null;
        }

        entity.ClickCount += 1;
        await dbContext.SaveChangesAsync(ct);

        await CacheUrlAsync(entity.ShortCode, entity.OriginalUrl, ct);

        return entity.OriginalUrl;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await dbContext.ShortUrls.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
        {
            return false;
        }

        dbContext.ShortUrls.Remove(entity);
        await dbContext.SaveChangesAsync(ct);

        try
        {
            await cache.RemoveAsync($"shorturl:{entity.ShortCode}", ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không xóa được Redis cache cho mã {Code}.", entity.ShortCode);
        }

        return true;
    }

    public async Task<SystemStatsDto> GetSystemStatsAsync(CancellationToken ct = default)
    {
        var dbConnected = await dbContext.Database.CanConnectAsync(ct);
        var redisConnected = false;

        try
        {
            await cache.SetStringAsync(
                "health:ping",
                "pong",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) },
                ct
            );
            var pong = await cache.GetStringAsync("health:ping", ct);
            redisConnected = pong == "pong";
        }
        catch
        {
            redisConnected = false;
        }

        var totalUrls = dbConnected ? await dbContext.ShortUrls.CountAsync(ct) : 0;
        var totalClicks = dbConnected ? await dbContext.ShortUrls.SumAsync(x => (long?)x.ClickCount, ct) ?? 0L : 0L;

        return new SystemStatsDto(
            TotalUrls: totalUrls,
            TotalClicks: totalClicks,
            InstanceName: InstanceName,
            Environment: environment.EnvironmentName,
            RedisConnected: redisConnected,
            DatabaseConnected: dbConnected
        );
    }

    private async Task CacheUrlAsync(string code, string originalUrl, CancellationToken ct)
    {
        try
        {
            await cache.SetStringAsync(
                $"shorturl:{code}",
                originalUrl,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6)
                },
                ct
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không ghi được Redis cache cho mã {Code}.", code);
        }
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var chars = new char[7];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }

            var candidate = new string(chars);
            var exists = await dbContext.ShortUrls.AnyAsync(x => x.ShortCode == candidate, ct);
            if (!exists)
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N")[..8];
    }

    private static ShortUrlDto MapToDto(ShortUrl entity, string baseUrl) =>
        new(
            entity.Id,
            entity.OriginalUrl,
            entity.ShortCode,
            $"{baseUrl.TrimEnd('/')}/r/{entity.ShortCode}",
            entity.ClickCount,
            entity.CreatedAt,
            entity.ExpiresAt,
            entity.IsActive,
            InstanceName
        );
}
