using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(notification => notification.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(notification => notification.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(notification => notification.Content).HasMaxLength(1600);
        builder.Property(notification => notification.ProviderMessageSid).HasMaxLength(34);
        builder.Property(notification => notification.ProviderStatus).HasMaxLength(32).IsRequired();
        builder.Property(notification => notification.IdempotencyKey).HasMaxLength(128);
        builder.HasIndex(notification => notification.ProviderMessageSid).IsUnique()
            .HasFilter("[ProviderMessageSid] IS NOT NULL");
        builder.HasIndex(notification => new { notification.ResendOfNotificationId, notification.IdempotencyKey })
            .IsUnique()
            .HasFilter("[ResendOfNotificationId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL");
        builder.HasOne<Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order>()
            .WithMany()
            .HasForeignKey(notification => notification.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ContactNumber>()
            .WithMany()
            .HasForeignKey(notification => notification.ContactNumberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
