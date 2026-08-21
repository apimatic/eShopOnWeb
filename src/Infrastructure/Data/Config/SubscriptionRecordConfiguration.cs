using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionRecordConfiguration : IEntityTypeConfiguration<SubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionRecord> builder)
    {
        builder.Property(record => record.UserId).IsRequired().HasMaxLength(450);
        builder.Property(record => record.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(record => record.SubscriptionReference).IsRequired().HasMaxLength(255);

        builder.HasIndex(record => new { record.UserId, record.ProductHandle }).IsUnique();
        builder.HasIndex(record => record.SubscriptionReference).IsUnique();
        builder.HasIndex(record => record.MaxioSubscriptionId).IsUnique();
    }
}
