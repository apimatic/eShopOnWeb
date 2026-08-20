using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
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
    public DbSet<ContactNumber> ContactNumbers { get; set; }
    public DbSet<OrderNotification> OrderNotifications { get; set; }
    public DbSet<OrderFulfillment> OrderFulfillments { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        foreach (var entityType in new[] { typeof(OrderNotification), typeof(OrderFulfillment) })
        {
            var mutable = builder.Model.FindEntityType(entityType);
            if (mutable is null)
            {
                continue;
            }

            foreach (var foreignKey in mutable.GetForeignKeys().ToList())
            {
                mutable.RemoveForeignKey(foreignKey);
            }
        }
    }

    public override int SaveChanges()
    {
        IgnoreOwnedPrimaryKeyMutations();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        IgnoreOwnedPrimaryKeyMutations();
        return base.SaveChangesAsync(cancellationToken);
    }

    // EF InMemory treats owned Address.OrderId as an identifying key. Reusing or
    // re-saving an Order graph can mark that key modified and throw; skip the mutation.
    private void IgnoreOwnedPrimaryKeyMutations()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (!entry.Metadata.IsOwned())
            {
                continue;
            }

            foreach (var property in entry.Properties)
            {
                if (property.Metadata.IsPrimaryKey() && property.IsModified)
                {
                    property.IsModified = false;
                }
            }
        }
    }
}
