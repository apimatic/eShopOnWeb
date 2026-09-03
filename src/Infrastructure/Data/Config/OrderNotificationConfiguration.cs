using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.ToTable("OrderNotifications");

        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.ProviderSid)
            .HasMaxLength(34);

        builder.Property(n => n.ProviderStatus)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(n => n.Body)
            .IsRequired();

        builder.Property(n => n.ToNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.FromNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.ProviderSid);
        builder.HasIndex(n => new { n.ResendOfNotificationId, n.IdempotencyKey });
    }
}
