using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionRecordConfiguration : IEntityTypeConfiguration<SubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionRecord> builder)
    {
        builder.ToTable("SubscriptionRecords");

        builder.Property(record => record.UserId).IsRequired().HasMaxLength(450);
        builder.Property(record => record.PlanHandle).IsRequired().HasMaxLength(255);
        builder.Property(record => record.CustomerReference).IsRequired().HasMaxLength(100);
        builder.Property(record => record.SubscriptionReference).IsRequired().HasMaxLength(255);
        builder.Property(record => record.PlanName).IsRequired().HasMaxLength(255);
        builder.Property(record => record.State).IsRequired().HasMaxLength(50);
        builder.Property(record => record.Currency).IsRequired().HasMaxLength(10);

        builder.HasIndex(record => new { record.UserId, record.PlanHandle }).IsUnique();
        builder.HasIndex(record => record.SubscriptionReference).IsUnique();
        builder.HasIndex(record => record.MaxioSubscriptionId).IsUnique();
    }
}
