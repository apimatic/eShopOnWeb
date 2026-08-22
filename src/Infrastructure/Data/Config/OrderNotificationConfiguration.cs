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

        builder.Property(n => n.DestinationNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.ProviderSid)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderStatus)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(n => n.Kind)
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.ProviderSid);
    }
}

public class NotificationResendAttemptConfiguration : IEntityTypeConfiguration<NotificationResendAttempt>
{
    public void Configure(EntityTypeBuilder<NotificationResendAttempt> builder)
    {
        builder.ToTable("NotificationResendAttempts");

        builder.Property(a => a.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(a => new { a.OriginalNotificationId, a.IdempotencyKey })
            .IsUnique();
    }
}
