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

        builder.Property(n => n.Kind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(n => n.Status)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(n => n.MessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.Property(n => n.Body)
            .HasMaxLength(1024);

        builder.Property(n => n.FailureReason)
            .HasMaxLength(512);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.BuyerId);
        // A given idempotency key identifies at most one resend (unique among non-null keys; the
        // provider's default filtered index handles the many-nulls case on relational stores).
        builder.HasIndex(n => n.IdempotencyKey)
            .IsUnique();
    }
}
