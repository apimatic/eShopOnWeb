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

        builder.Property(n => n.Body)
            .IsRequired()
            .HasMaxLength(1600);

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(34);

        builder.Property(n => n.ProviderStatus)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(n => n.ProviderErrorMessage)
            .HasMaxLength(512);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.ProviderMessageSid);
        builder.HasIndex(n => n.CreatedAt);
    }
}

public class NotificationResendRecordConfiguration : IEntityTypeConfiguration<NotificationResendRecord>
{
    public void Configure(EntityTypeBuilder<NotificationResendRecord> builder)
    {
        builder.ToTable("NotificationResendRecords");

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => new { r.SourceNotificationId, r.IdempotencyKey })
            .IsUnique();
    }
}
