using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.ToTable("OrderNotifications");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.Kind)
            .IsRequired();

        builder.Property(n => n.Body)
            .IsRequired()
            .HasMaxLength(1600);

        builder.Property(n => n.DestinationNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderStatus)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(n => n.ProviderErrorCode)
            .HasMaxLength(32);

        builder.Property(n => n.ResendIdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.ProviderMessageSid);
        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => new { n.SourceNotificationId, n.ResendIdempotencyKey });
    }
}
