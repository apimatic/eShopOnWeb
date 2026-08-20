using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

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

        builder.Property(record => record.NormalizedProductHandle)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(record => record.ProviderReference)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(record => record.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(record => record.FailureMessage)
            .HasMaxLength(500);

        builder.HasIndex(record => new { record.UserId, record.NormalizedProductHandle })
            .IsUnique();

        builder.HasIndex(record => record.ProviderReference)
            .IsUnique();
    }
}

