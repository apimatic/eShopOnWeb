using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionBillingRecordConfiguration : IEntityTypeConfiguration<SubscriptionBillingRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionBillingRecord> builder)
    {
        builder.ToTable("SubscriptionBillingRecords");

        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ProductHandle).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CustomerReference).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SubscriptionReference).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.Property(x => x.UpdatedAt).IsConcurrencyToken();

        builder.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
        builder.HasIndex(x => x.SubscriptionReference).IsUnique();
    }
}
