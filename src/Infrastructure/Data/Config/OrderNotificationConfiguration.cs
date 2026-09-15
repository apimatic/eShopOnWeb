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
            .HasMaxLength(30);

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.Status)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(200);

        builder.HasIndex(n => n.OrderId);

        // A given idempotency key may produce at most one message. (Filter is SQL Server specific and
        // ignored by the in-memory provider.)
        builder.HasIndex(n => n.IdempotencyKey)
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");
    }
}
