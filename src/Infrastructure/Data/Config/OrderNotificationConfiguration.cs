using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

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
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.ContentRedacted)
            .IsRequired();

        builder.Property(n => n.DestinationCanonicalNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(34);

        builder.Property(n => n.ProviderStatus)
            .HasMaxLength(32);

        builder.Property(n => n.ProviderErrorMessage)
            .HasMaxLength(512);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.ProviderMessageSid);
        builder.HasIndex(n => new { n.ResentFromNotificationId, n.IdempotencyKey });
        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.BuyerId);
    }
}
