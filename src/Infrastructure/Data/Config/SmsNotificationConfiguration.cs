using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SmsNotificationConfiguration : IEntityTypeConfiguration<SmsNotification>
{
    public void Configure(EntityTypeBuilder<SmsNotification> builder)
    {
        builder.ToTable("SmsNotifications");

        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.ToNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.Kind)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(n => n.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(n => n.ProviderMessageId)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderStatus)
            .HasMaxLength(32);

        builder.Property(n => n.ErrorMessage)
            .HasMaxLength(512);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.ProviderMessageId);
        builder.HasIndex(n => n.IdempotencyKey);
    }
}
