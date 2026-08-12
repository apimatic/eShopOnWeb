using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(n => n.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.ToPhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.Kind)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(n => n.DeliveryStatus)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.ErrorCode)
            .HasMaxLength(128);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.Property(n => n.MessageBody)
            .HasMaxLength(1600);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.OwnerId);
        builder.HasIndex(n => n.IdempotencyKey);
    }
}
