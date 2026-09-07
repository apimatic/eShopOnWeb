using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.Property(s => s.UserId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.ProductHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.State)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.IntervalUnit)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.PriceInCents)
            .HasPrecision(18, 2);

        builder.HasIndex(s => new { s.UserId, s.MaxioSubscriptionId })
            .IsUnique();

        builder.HasIndex(s => s.MaxioCustomerId);
    }
}
