using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.ToTable("OrderNotifications");

        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(notification => notification.DestinationPhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(notification => notification.Body)
            .HasMaxLength(1600)
            .IsRequired(false);

        builder.Property(notification => notification.ContentRedacted)
            .IsRequired();

        builder.Property(notification => notification.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(notification => notification.ProviderStatus)
            .HasMaxLength(64);

        builder.Property(notification => notification.ProviderErrorCode)
            .HasMaxLength(32);

        builder.Property(notification => notification.Kind)
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.HasIndex(notification => notification.OrderId);
        builder.HasIndex(notification => notification.ProviderMessageSid);
    }
}
