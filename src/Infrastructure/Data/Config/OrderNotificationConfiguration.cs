using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.ToTable("OrderNotifications");

        builder.Property(notification => notification.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(notification => notification.DestinationNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(notification => notification.Body)
            .HasMaxLength(1600);

        builder.Property(notification => notification.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(notification => notification.ProviderStatus)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(notification => notification.LocalFailure)
            .HasMaxLength(64);

        builder.HasIndex(notification => notification.OrderId);
        builder.HasIndex(notification => notification.ProviderMessageSid);
    }
}
