using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionRecordConfiguration : IEntityTypeConfiguration<MaxioSubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionRecord> builder)
    {
        builder.ToTable("MaxioSubscriptions");

        builder.Property(record => record.UserId).IsRequired().HasMaxLength(450);
        builder.Property(record => record.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(record => record.CustomerReference).IsRequired().HasMaxLength(255);
        builder.Property(record => record.SubscriptionReference).IsRequired().HasMaxLength(255);
        builder.Property(record => record.UpdatedAt).IsRequired();

        builder.HasIndex(record => new { record.UserId, record.ProductHandle }).IsUnique();
        builder.HasIndex(record => record.SubscriptionReference).IsUnique();
    }
}
