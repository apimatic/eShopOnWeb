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
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderStatus)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderErrorMessage)
            .HasMaxLength(512);

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.ProviderMessageSid);
    }
}

public class NotificationResendConfiguration : IEntityTypeConfiguration<NotificationResend>
{
    public void Configure(EntityTypeBuilder<NotificationResend> builder)
    {
        builder.ToTable("NotificationResends");

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => new { r.SourceNotificationId, r.IdempotencyKey })
            .IsUnique();
    }
}
