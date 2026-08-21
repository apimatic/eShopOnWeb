using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionProvisioningRecordConfiguration
    : IEntityTypeConfiguration<SubscriptionProvisioningRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionProvisioningRecord> builder)
    {
        builder.ToTable("SubscriptionProvisioning");

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).UseHiLo("subscription_provisioning_hilo");
        builder.Property(record => record.UserId).IsRequired().HasMaxLength(450);
        builder.Property(record => record.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(record => record.CustomerReference).IsRequired().HasMaxLength(100);
        builder.Property(record => record.SubscriptionReference).IsRequired().HasMaxLength(100);
        builder.Property(record => record.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(record => record.LastErrorCode).HasMaxLength(100);
        builder.Property(record => record.ConcurrencyToken).IsConcurrencyToken();

        builder.HasIndex(record => new { record.UserId, record.ProductHandle }).IsUnique();
        builder.HasIndex(record => record.SubscriptionReference).IsUnique();
    }
}
