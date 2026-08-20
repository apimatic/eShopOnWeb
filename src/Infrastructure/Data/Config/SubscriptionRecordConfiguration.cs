using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionRecordConfiguration : IEntityTypeConfiguration<SubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionRecord> builder)
    {
        builder.ToTable("SubscriptionRecords");

        builder.Property(record => record.UserId)
            .IsRequired()
            .HasMaxLength(450);
        builder.Property(record => record.ProductHandle)
            .IsRequired()
            .HasMaxLength(255);
        builder.Property(record => record.CustomerReference)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(record => record.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(record => record.SubscriptionReference).IsUnique();
        builder.HasIndex(record => new { record.UserId, record.ProductHandle }).IsUnique();
    }
}
