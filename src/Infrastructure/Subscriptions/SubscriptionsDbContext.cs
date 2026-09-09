using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

/// <summary>
/// Owns the local userId-to-subscription mapping. Kept separate from the
/// catalog/identity contexts so the subscription capability stays additive.
/// </summary>
public class SubscriptionsDbContext : DbContext
{
    public SubscriptionsDbContext(DbContextOptions<SubscriptionsDbContext> options)
        : base(options)
    {
    }

    public DbSet<SubscriptionRecord> SubscriptionRecords => Set<SubscriptionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SubscriptionRecord>(entity =>
        {
            entity.ToTable("SubscriptionRecords");
            entity.HasIndex(r => new { r.UserId, r.PlanHandle });
            entity.HasIndex(r => r.MaxioSubscriptionId).IsUnique();
        });
    }
}
