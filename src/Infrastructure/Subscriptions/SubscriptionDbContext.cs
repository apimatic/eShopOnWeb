using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public sealed class SubscriptionDbContext : DbContext
{
    public SubscriptionDbContext(DbContextOptions<SubscriptionDbContext> options) : base(options) { }

    public DbSet<SubscriptionRecord> SubscriptionRecords => Set<SubscriptionRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        var record = builder.Entity<SubscriptionRecord>();
        record.ToTable("SubscriptionRecords");
        record.HasKey(item => item.Id);
        record.Property(item => item.UserId).HasMaxLength(450).IsRequired();
        record.Property(item => item.ProductHandle).HasMaxLength(255).IsRequired();
        record.Property(item => item.SubscriptionReference).HasMaxLength(800).IsRequired();
        record.Property(item => item.CreationToken).HasMaxLength(36).IsRequired();
        record.HasIndex(item => new { item.UserId, item.ProductHandle }).IsUnique();
        record.HasIndex(item => item.SubscriptionReference).IsUnique();
    }
}
