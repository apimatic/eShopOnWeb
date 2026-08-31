using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(n => n.BuyerId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(n => n.ToNumber)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(n => n.NotificationType)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(n => n.ProviderMessageId)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderStatus)
            .HasMaxLength(32);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.ProviderMessageId);
        builder.HasIndex(n => n.IdempotencyKey);
    }
}
