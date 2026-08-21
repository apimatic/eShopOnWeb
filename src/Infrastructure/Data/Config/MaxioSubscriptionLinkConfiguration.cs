using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class MaxioSubscriptionLinkConfiguration : IEntityTypeConfiguration<MaxioSubscriptionLink>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionLink> builder)
    {
        builder.Property(link => link.UserId).IsRequired().HasMaxLength(450);
        builder.Property(link => link.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(link => link.PricePointHandle).IsRequired().HasMaxLength(255);
        builder.Property(link => link.SubscriptionReference).IsRequired().HasMaxLength(80);
        builder.Property(link => link.Status).IsRequired().HasMaxLength(20);
        builder.Property(link => link.LastSafeErrorCode).HasMaxLength(80);
        builder.Property(link => link.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(link => new
        {
            link.UserId,
            link.ProductHandle,
            link.PricePointHandle
        }).IsUnique();
        builder.HasIndex(link => link.SubscriptionReference).IsUnique();
    }
}
