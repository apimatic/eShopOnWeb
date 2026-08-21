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

        builder.Property(n => n.DestinationNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(n => n.Body)
            .IsRequired()
            .HasMaxLength(1600);

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(34);

        builder.Property(n => n.ProviderStatus)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.ProviderMessageSid);
    }
}
