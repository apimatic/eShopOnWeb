using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(n => n.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.Kind)
            .IsRequired()
            .HasMaxLength(30)
            .HasConversion<string>();

        builder.Property(n => n.ToNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.MessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.Status)
            .HasMaxLength(30);

        builder.Property(n => n.ErrorMessage)
            .HasMaxLength(512);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.OrderId);

        // A resend idempotency key is used at most once. EF applies a default "IS NOT NULL" filter for a
        // nullable unique index, so the many notifications without a key do not collide.
        builder.HasIndex(n => n.IdempotencyKey)
            .IsUnique();
    }
}
