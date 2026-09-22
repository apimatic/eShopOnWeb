using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SmsNotificationConfiguration : IEntityTypeConfiguration<SmsNotification>
{
    public void Configure(EntityTypeBuilder<SmsNotification> builder)
    {
        builder.Property(n => n.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.Recipient)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.Kind)
            .HasConversion<int>();

        builder.Property(n => n.State)
            .HasConversion<int>();

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.ProviderSid)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderStatus)
            .HasMaxLength(32);

        builder.Property(n => n.ErrorMessage)
            .HasMaxLength(512);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.OwnerId);
        builder.HasIndex(n => n.ProviderSid);
    }
}
