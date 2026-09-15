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

        builder.Property(n => n.Type)
            .HasConversion<int>();

        builder.Property(n => n.DeliveryStatus)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.ErrorMessage)
            .HasMaxLength(512);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.BuyerId);
        builder.HasIndex(n => n.IdempotencyKey);
    }
}
