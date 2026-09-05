using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .UseHiLo("subscription_hilo")
            .IsRequired();

        builder.Property(s => s.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(s => s.ProductHandle)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.State)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.CurrentPrice)
            .HasPrecision(18, 2);

        builder.Property(s => s.NextBillingAt)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.MaxioSubscriptionId);
        builder.HasIndex(s => s.MaxioCustomerId);
    }
}
