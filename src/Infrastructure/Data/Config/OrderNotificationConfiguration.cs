using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.ToTable("OrderNotifications");

        builder.Property(n => n.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(n => n.Kind).IsRequired().HasMaxLength(64);
        builder.Property(n => n.Destination).HasMaxLength(32);
        builder.Property(n => n.Body).HasMaxLength(1600);
        builder.Property(n => n.ProviderSid).HasMaxLength(64);
        builder.Property(n => n.Status).HasMaxLength(64);
        builder.Property(n => n.ErrorMessage).HasMaxLength(1024);
        builder.Property(n => n.Direction).HasMaxLength(32);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.ProviderSid);
    }
}
