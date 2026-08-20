using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionRecordConfiguration : IEntityTypeConfiguration<SubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionRecord> builder)
    {
        builder.ToTable("SubscriptionRecords");

        builder.Property(record => record.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(record => record.ProductHandle)
            .HasMaxLength(255)
            .IsRequired();

        builder.HasIndex(record => new { record.UserId, record.ProductHandle })
            .IsUnique();

        builder.HasIndex(record => record.MaxioSubscriptionId)
            .IsUnique()
            .HasFilter("[MaxioSubscriptionId] IS NOT NULL");
    }
}
