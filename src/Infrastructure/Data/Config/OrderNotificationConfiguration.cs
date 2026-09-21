using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.ToNumberE164)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(n => n.DeliveryState)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderStatus)
            .HasMaxLength(32);

        builder.Property(n => n.ProviderErrorMessage)
            .HasMaxLength(512);

        builder.Property(n => n.ResendIdempotencyKey)
            .HasMaxLength(128);

        // The atomic guarantee behind resend idempotency: a second row under the same key is rejected by the
        // store, and the service catches that rejection. Filtered so the many null keys do not collide.
        // (Note: the mandated in-memory provider does not enforce indexes; the SQL Server path does.)
        builder.HasIndex(n => n.ResendIdempotencyKey)
            .IsUnique()
            .HasFilter("[ResendIdempotencyKey] IS NOT NULL");

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.BuyerId);
    }
}
