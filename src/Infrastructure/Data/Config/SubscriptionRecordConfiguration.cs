using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionRecordConfiguration : IEntityTypeConfiguration<SubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionRecord> builder)
    {
        builder.ToTable("SubscriptionRecords");

        builder.Property(record => record.UserId).IsRequired().HasMaxLength(450);
        builder.Property(record => record.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(record => record.CustomerReference).IsRequired().HasMaxLength(255);
        builder.Property(record => record.SubscriptionReference).IsRequired().HasMaxLength(255);
        builder.Property(record => record.SubscriptionUniquenessToken).IsRequired().HasMaxLength(64);
        builder.Property(record => record.ProductName).IsRequired().HasMaxLength(255);
        builder.Property(record => record.Currency).HasMaxLength(3);
        builder.Property(record => record.IntervalUnit).IsRequired().HasMaxLength(16);
        builder.Property(record => record.State).IsRequired().HasMaxLength(32);

        builder.HasIndex(record => new { record.UserId, record.ProductHandle }).IsUnique();
        builder.HasIndex(record => record.CustomerReference);
        builder.HasIndex(record => record.SubscriptionReference).IsUnique();
        builder.HasIndex(record => record.MaxioSubscriptionId).IsUnique();
    }
}
