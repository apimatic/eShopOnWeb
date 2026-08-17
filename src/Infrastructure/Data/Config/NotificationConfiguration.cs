using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(n => n.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.ToPhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.Kind)
            .HasConversion<int>();

        builder.Property(n => n.State)
            .HasConversion<int>();

        builder.Property(n => n.Body)
            .HasMaxLength(1600); // Twilio single-message body ceiling

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderStatus)
            .HasMaxLength(32);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.OwnerId);
        builder.HasIndex(n => n.ProviderMessageSid);
        builder.HasIndex(n => n.IdempotencyKey);
    }
}
