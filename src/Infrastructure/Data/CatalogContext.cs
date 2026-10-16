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
    public DbSet<BillingClaim> BillingClaims { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ConfigureBillingClaims(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    public static void ConfigureBillingClaims(ModelBuilder builder)
    {
        builder.Entity<BillingClaim>(claim =>
        {
            claim.ToTable("BillingClaims");
            claim.HasKey(x => x.Id);
            claim.Property(x => x.Id).HasMaxLength(64);
            claim.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            claim.Property(x => x.PlanHandle).HasMaxLength(255).IsRequired();
            claim.Property(x => x.Reference).HasMaxLength(80).IsRequired();
            claim.Property(x => x.State).HasMaxLength(16).IsRequired();
        });
    }
}
