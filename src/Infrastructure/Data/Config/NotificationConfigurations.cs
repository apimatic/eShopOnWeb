using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperContactNumberConfiguration : IEntityTypeConfiguration<ShopperContactNumber>
{
    public void Configure(EntityTypeBuilder<ShopperContactNumber> builder)
    {
        builder.HasKey(n => n.Id);

        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.CanonicalNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasIndex(n => new { n.BuyerId, n.CanonicalNumber })
            .IsUnique();
    }
}

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.HasKey(n => n.Id);

        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.DestinationNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.ProviderSid)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderStatus)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(n => n.ProviderErrorCode)
            .HasMaxLength(32);

        builder.Property(n => n.ProviderErrorMessage)
            .HasMaxLength(512);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.ProviderSid);
        builder.HasIndex(n => n.BuyerId);
    }
}

public class NotificationResendIdempotencyConfiguration : IEntityTypeConfiguration<NotificationResendIdempotency>
{
    public void Configure(EntityTypeBuilder<NotificationResendIdempotency> builder)
    {
        builder.HasKey(n => n.Id);

        builder.Property(n => n.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(n => n.IdempotencyKey)
            .IsUnique();
    }
}
