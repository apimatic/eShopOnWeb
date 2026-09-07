using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.UserId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.ProductHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.State)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(s => s.PriceInCents);

        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.UpdatedAt);

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.MaxioSubscriptionId);
    }
}
