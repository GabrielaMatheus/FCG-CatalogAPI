using FiapCloudGames.CatalogApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace FiapCloudGames.CatalogApi.Data;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();
    public DbSet<LibraryItem> LibraryItems => Set<LibraryItem>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Game>(entity =>
        {
            entity.Property(g => g.Name).IsRequired().HasMaxLength(150);
            entity.Property(g => g.Description).HasMaxLength(1000);
            entity.Property(g => g.Price).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<LibraryItem>(entity =>
        {
            entity.HasIndex(l => new { l.UserId, l.GameId }).IsUnique();
            entity.HasOne(l => l.Game)
                .WithMany()
                .HasForeignKey(l => l.GameId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(o => o.Price).HasColumnType("decimal(18,2)");
            entity.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        });

        base.OnModelCreating(modelBuilder);
    }
}
