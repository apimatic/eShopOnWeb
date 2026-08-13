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

        builder.Property(n => n.ToPhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.DeliveryStatus)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.Type)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(n => n.MessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.Property(n => n.Body)
            .HasMaxLength(1024);

        builder.HasIndex(n => n.OrderId);
    }
}
