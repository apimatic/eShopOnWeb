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
            .HasMaxLength(100);

        builder.Property(s => s.SubscriptionState)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.PriceInCents)
            .IsRequired();

        builder.HasKey(s => s.Id);
    }
}
