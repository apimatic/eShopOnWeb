using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(n => n.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(n => n.ToNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.DeliveryStatus)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.FailureReason)
            .HasMaxLength(128);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.OwnerId);
        builder.HasIndex(n => n.ProviderMessageSid);

        // A given idempotency key maps to at most one message. On a relational provider EF applies the
        // default "IS NOT NULL" filter, so the many rows with no key (non-resend messages) do not collide.
        builder.HasIndex(n => n.IdempotencyKey)
            .IsUnique();
    }
}
