using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.NotificationType)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(n => n.MessageSid)
            .HasMaxLength(34);

        builder.Property(n => n.Status)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.ErrorCode)
            .HasMaxLength(16);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.MessageSid);
        builder.HasIndex(n => n.IdempotencyKey);
    }
}
