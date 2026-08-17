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

        builder.Property(n => n.ToNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderStatus)
            .HasMaxLength(40);

        builder.Property(n => n.ErrorMessage)
            .HasMaxLength(1024);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(200);

        builder.Property(n => n.Kind)
            .HasConversion<string>()
            .HasMaxLength(40);

        builder.Property(n => n.DeliveryStatus)
            .HasConversion<string>()
            .HasMaxLength(40);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.BuyerId);
        builder.HasIndex(n => n.ProviderMessageSid);
        // Unique only among rows that actually carry an idempotency key (re-sends); the many rows
        // with no key are unconstrained. The in-memory provider ignores the filter and enforcement
        // is also done in the service before sending.
        builder.HasIndex(n => n.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
    }
}
