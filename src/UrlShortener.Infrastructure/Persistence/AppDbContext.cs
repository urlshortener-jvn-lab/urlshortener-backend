using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain.Entities;

namespace UrlShortener.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ShortUrl> ShortUrls => Set<ShortUrl>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ShortUrl>(entity =>
        {
            entity.ToTable("short_urls");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OriginalUrl).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.ShortCode).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.ShortCode).IsUnique();
            entity.HasIndex(x => x.CreatedAt);
        });
    }
}
