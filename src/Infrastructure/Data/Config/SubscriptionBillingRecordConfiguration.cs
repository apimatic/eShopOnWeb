using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionBillingRecordConfiguration : IEntityTypeConfiguration<SubscriptionBillingRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionBillingRecord> builder)
    {
        builder.ToTable("SubscriptionBillingRecords");

        builder.HasKey(record => record.Id);
        builder.HasIndex(record => new { record.UserId, record.ProductHandle }).IsUnique();
        builder.HasIndex(record => record.SubscriptionReference).IsUnique();

        builder.Property(record => record.UserId).IsRequired().HasMaxLength(450);
        builder.Property(record => record.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(record => record.CustomerReference).IsRequired().HasMaxLength(80);
        builder.Property(record => record.SubscriptionReference).IsRequired().HasMaxLength(80);
        builder.Property(record => record.Status).IsRequired();
        builder.Property(record => record.Version).IsRowVersion();
    }
}
