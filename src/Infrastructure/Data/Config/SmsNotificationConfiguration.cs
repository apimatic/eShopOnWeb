using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SmsNotificationConfiguration : IEntityTypeConfiguration<SmsNotification>
{
    public void Configure(EntityTypeBuilder<SmsNotification> builder)
    {
        builder.ToTable("SmsNotifications");

        builder.Property(n => n.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(n => n.ToNumber).IsRequired().HasMaxLength(20);
        builder.Property(n => n.Body).HasMaxLength(1600);
        builder.Property(n => n.ProviderSid).HasMaxLength(64);
        builder.Property(n => n.Status).IsRequired().HasMaxLength(32);
        builder.Property(n => n.IdempotencyKey).HasMaxLength(128);
        builder.Property(n => n.Kind).IsRequired();

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.IdempotencyKey);
    }
}
