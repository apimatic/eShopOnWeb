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
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.ContentRedacted)
            .IsRequired();

        builder.Property(n => n.DestinationCanonical)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.ProviderSid)
            .HasMaxLength(64);

        builder.Property(n => n.Status)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(n => n.ErrorMessage)
            .HasMaxLength(512);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.ProviderSid);
        builder.HasIndex(n => new { n.ParentNotificationId, n.IdempotencyKey });
    }
}
