using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Body).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(34);
        builder.Property(x => x.ProviderStatus).IsRequired().HasMaxLength(32);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128);
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.ContactNumberId);
        builder.HasIndex(x => x.ProviderMessageSid);
        builder.HasIndex(x => new { x.ResendOfNotificationId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("[ResendOfNotificationId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL");
    }
}
