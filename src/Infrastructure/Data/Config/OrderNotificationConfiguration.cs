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

        builder.Property(n => n.Kind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(n => n.DestinationNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(n => n.Body)
            .IsRequired(false)
            .HasMaxLength(1600);

        builder.Property(n => n.ContentRedacted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(n => n.ProviderSid)
            .HasMaxLength(34);

        builder.Property(n => n.ProviderStatus)
            .HasMaxLength(32);

        builder.Property(n => n.SendFailure)
            .HasMaxLength(128);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.ProviderSid);
    }
}

public class NotificationResendKeyConfiguration : IEntityTypeConfiguration<NotificationResendKey>
{
    public void Configure(EntityTypeBuilder<NotificationResendKey> builder)
    {
        builder.ToTable("NotificationResendKeys");

        builder.Property(k => k.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(k => new { k.SourceNotificationId, k.IdempotencyKey }).IsUnique();
    }
}
