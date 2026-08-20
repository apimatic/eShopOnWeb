using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionBillingRecordConfiguration : IEntityTypeConfiguration<SubscriptionBillingRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionBillingRecord> builder)
    {
        builder.ToTable("SubscriptionBillingRecords");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.UserId).HasMaxLength(450).IsRequired();
        builder.Property(record => record.ProductHandle).HasMaxLength(255).IsRequired();
        builder.Property(record => record.SubscriptionReference).HasMaxLength(255).IsRequired();
        builder.Property(record => record.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(record => record.LeaseToken).IsConcurrencyToken();
        builder.HasIndex(record => new { record.UserId, record.ProductHandle }).IsUnique();
        builder.HasIndex(record => record.SubscriptionReference).IsUnique();
    }
}
