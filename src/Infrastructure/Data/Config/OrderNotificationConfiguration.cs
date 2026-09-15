using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Body).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(64);
        builder.Property(x => x.ProviderStatus).IsRequired().HasMaxLength(40);
        builder.Property(x => x.ResendIdempotencyKey).HasMaxLength(128);
        builder.HasIndex(x => x.ProviderMessageSid).IsUnique().HasFilter("[ProviderMessageSid] IS NOT NULL");
        builder.HasIndex(x => new { x.OriginalNotificationId, x.ResendIdempotencyKey })
            .IsUnique()
            .HasFilter("[OriginalNotificationId] IS NOT NULL AND [ResendIdempotencyKey] IS NOT NULL");
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.ContactNumberId);
    }
}
