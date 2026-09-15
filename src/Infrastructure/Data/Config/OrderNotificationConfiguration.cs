using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.ToTable("OrderNotifications");
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Body).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(64);
        builder.Property(x => x.ProviderStatus).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ProviderFrom).HasMaxLength(32);
        builder.Property(x => x.ProviderMessagingServiceSid).HasMaxLength(64);
        builder.Property(x => x.ProviderDateCreated).HasMaxLength(64);
        builder.Property(x => x.ProviderDateSent).HasMaxLength(64);
        builder.Property(x => x.ProviderDateUpdated).HasMaxLength(64);
        builder.Property(x => x.ProviderErrorMessage).HasMaxLength(1000);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200);
        builder.HasIndex(x => x.ProviderMessageSid);
        builder.HasIndex(x => new { x.OrderId, x.CreatedAt });
        builder.HasIndex(x => new { x.ResendOfNotificationId, x.IdempotencyKey }).IsUnique();
    }
}
