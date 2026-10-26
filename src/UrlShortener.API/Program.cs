using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using UrlShortener.Application.DTOs;
using UrlShortener.Application.Interfaces;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Services;
using UrlShortener.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

var postgresConnection =
    builder.Configuration.GetConnectionString("Postgres")
    ?? builder.Configuration.GetConnectionString("urlshortener")
    ?? "Host=localhost;Port=5432;Database=urlshortener;Username=postgres;Password=postgres";

var redisConnection =
    builder.Configuration.GetConnectionString("Redis")
    ?? builder.Configuration.GetConnectionString("redis");

builder.AddServiceDefaults(postgresConnection, redisConnection);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(postgresConnection).UseSnakeCaseNamingConvention()
);

if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "urlshortener:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddScoped<IShortUrlService, ShortUrlService>();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
    );
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("shorten", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }
        )
    );
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await db.Database.EnsureCreatedAsync();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Chưa kết nối được PostgreSQL lúc khởi động. Kiểm tra container/service Postgres.");
    }
}

app.UseForwardedHeaders();
app.UseCors();
app.UseRateLimiter();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapScalarApiReference(options =>
{
    options
        .WithTitle("URL Shortener API — Scalar UI")
        .WithTheme(ScalarTheme.BluePlanet)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

app.MapDefaultEndpoints();

var api = app.MapGroup("/api/urls").WithTags("Short URLs");

api.MapGet("", async (HttpContext http, IShortUrlService service, CancellationToken ct) =>
{
    var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
    var list = await service.GetRecentAsync(baseUrl, 50, ct);
    return TypedResults.Ok(list);
})
.WithName("GetRecentShortUrls")
.WithSummary("Lấy danh sách 50 link rút gọn gần nhất");

api.MapPost("", async (
    CreateShortUrlRequest request,
    HttpContext http,
    IShortUrlService service,
    CancellationToken ct
) =>
{
    try
    {
        var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
        var created = await service.CreateAsync(request, baseUrl, ct);
        return Results.Created($"/api/urls/{created.Id}", created);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { message = ex.Message });
    }
})
.RequireRateLimiting("shorten")
.WithName("CreateShortUrl")
.WithSummary("Tạo mới một link rút gọn (có Rate Limit 30 req/phút/IP)");

api.MapDelete("/{id:guid}", async (Guid id, IShortUrlService service, CancellationToken ct) =>
{
    var deleted = await service.DeleteAsync(id, ct);
    return deleted ? Results.NoContent() : Results.NotFound();
})
.WithName("DeleteShortUrl")
.WithSummary("Xóa link rút gọn và xóa luôn key trên Redis Cache");

app.MapGet("/api/system/stats", async (IShortUrlService service, CancellationToken ct) =>
{
    var stats = await service.GetSystemStatsAsync(ct);
    return TypedResults.Ok(stats);
})
.WithTags("System & Infra")
.WithName("GetSystemStats")
.WithSummary("Kiểm tra trạng thái kết nối PostgreSQL, Redis, Instance Name và tổng lượt click");

app.MapGet("/r/{code}", async (string code, IShortUrlService service, CancellationToken ct) =>
{
    var targetUrl = await service.ResolveAndTrackClickAsync(code, ct);
    return targetUrl is not null
        ? Results.Redirect(targetUrl, permanent: false)
        : Results.NotFound(new { message = "Link rút gọn không tồn tại hoặc đã hết hạn." });
})
.WithTags("Redirect")
.WithName("RedirectShortCode")
.WithSummary("Giải mã code qua Redis Cache-Aside (fallback PostgreSQL) và chuyển hướng 302");

app.Run();
