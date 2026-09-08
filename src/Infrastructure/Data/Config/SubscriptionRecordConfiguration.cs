using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionRecordConfiguration : IEntityTypeConfiguration<SubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionRecord> builder)
    {
        builder.ToTable("Subscriptions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.BuyerId)
            .HasMaxLength(256)
            .IsRequired();
        builder.Property(s => s.CustomerReference)
            .HasMaxLength(320)
            .IsRequired();
        builder.Property(s => s.ProductHandle)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(s => s.ProductName)
            .HasMaxLength(500)
            .IsRequired();
        builder.Property(s => s.IntervalUnit)
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(s => s.State)
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(s => s.Currency)
            .HasMaxLength(10)
            .IsRequired();

        // One live subscription per buyer + plan; double-click protection at the data layer.
        builder.HasIndex(s => new { s.BuyerId, s.ProductHandle })
            .IsUnique();
    }
}
