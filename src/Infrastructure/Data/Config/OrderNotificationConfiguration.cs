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

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderStatus)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(n => n.ErrorMessage)
            .HasMaxLength(1024);

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.Destination)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.Type)
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.ProviderMessageSid);
    }
}
