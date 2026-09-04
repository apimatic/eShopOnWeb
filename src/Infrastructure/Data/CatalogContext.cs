using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class CatalogContext : DbContext
{
    #pragma warning disable CS8618 // Required by Entity Framework
    public CatalogContext(DbContextOptions<CatalogContext> options) : base(options) {}

    public DbSet<Basket> Baskets { get; set; }
    public DbSet<CatalogItem> CatalogItems { get; set; }
    public DbSet<CatalogBrand> CatalogBrands { get; set; }
    public DbSet<CatalogType> CatalogTypes { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<BasketItem> BasketItems { get; set; }
    public DbSet<SubscriptionEnrollment> SubscriptionEnrollments { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        builder.Entity<SubscriptionEnrollment>(entity =>
        {
            entity.HasKey(enrollment => enrollment.Id);
            entity.Property(enrollment => enrollment.UserId).HasMaxLength(450).IsRequired();
            entity.Property(enrollment => enrollment.ProductHandle).HasMaxLength(255).IsRequired();
            entity.Property(enrollment => enrollment.UniquenessToken).HasMaxLength(100).IsRequired();
            entity.HasIndex(enrollment => new { enrollment.UserId, enrollment.ProductHandle }).IsUnique();
        });
    }
}
