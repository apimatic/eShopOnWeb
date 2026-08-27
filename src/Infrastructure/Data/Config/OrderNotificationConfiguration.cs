using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(n => n.ProviderMessageSid).HasMaxLength(64);
        builder.Property(n => n.ProviderStatus).HasMaxLength(32);
        builder.Property(n => n.ProviderErrorMessage).HasMaxLength(1024);
        builder.Property(n => n.Body).HasMaxLength(1600);
        builder.Property(n => n.IdempotencyKey).HasMaxLength(128);
        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => new { n.ResendOfNotificationId, n.IdempotencyKey });
    }
}
