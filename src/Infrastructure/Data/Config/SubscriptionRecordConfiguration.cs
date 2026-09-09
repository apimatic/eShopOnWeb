using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionRecordConfiguration : IEntityTypeConfiguration<SubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionRecord> builder)
    {
        builder.Property(sr => sr.UserId)
            .HasMaxLength(450)
            .IsRequired(true);
        builder.Property(sr => sr.MaxioReference)
            .HasMaxLength(100)
            .IsRequired(true);
        builder.Property(sr => sr.ProductHandle)
            .HasMaxLength(100)
            .IsRequired(true);
        builder.Property(sr => sr.ProductName)
            .HasMaxLength(255)
            .IsRequired(true);
        builder.Property(sr => sr.State)
            .HasMaxLength(50)
            .IsRequired(true);
        builder.HasIndex(sr => sr.UserId);
        builder.HasIndex(sr => sr.MaxioSubscriptionId);
    }
}
