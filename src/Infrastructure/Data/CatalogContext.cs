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
    public DbSet<BillingClaim> BillingClaims { get; set; }
    public DbSet<CatalogItem> CatalogItems { get; set; }
    public DbSet<CatalogBrand> CatalogBrands { get; set; }
    public DbSet<CatalogType> CatalogTypes { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<BasketItem> BasketItems { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<BillingClaim>(claim =>
        {
            claim.HasKey(x => x.Id);
            claim.Property(x => x.Id).HasMaxLength(80);
            claim.Property(x => x.Kind).HasMaxLength(20).IsRequired();
            claim.Property(x => x.CustomerReference).HasMaxLength(80).IsRequired();
            claim.Property(x => x.ProductHandle).HasMaxLength(255);
        });
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
