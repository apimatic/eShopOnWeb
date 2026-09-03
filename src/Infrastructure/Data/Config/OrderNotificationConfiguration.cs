using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(n => n.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.To)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.Kind)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(n => n.DeliveryStatus).HasMaxLength(32);
        builder.Property(n => n.ProviderSid).HasMaxLength(64);
        builder.Property(n => n.ProviderFrom).HasMaxLength(32);
        builder.Property(n => n.ResendIdempotencyKey).HasMaxLength(128);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.OwnerId);
        builder.HasIndex(n => n.ResendIdempotencyKey);
    }
}
