using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(n => n.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(n => n.Recipient).IsRequired().HasMaxLength(32);
        builder.Property(n => n.Body).HasMaxLength(1600);
        builder.Property(n => n.Kind).HasConversion<int>();
        builder.Property(n => n.State).HasConversion<int>();
        builder.Property(n => n.ProviderMessageSid).HasMaxLength(64);
        builder.Property(n => n.ProviderStatus).HasMaxLength(32);
        builder.Property(n => n.ProviderErrorMessage).HasMaxLength(512);
        builder.Property(n => n.ResendIdempotencyKey).HasMaxLength(128);

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.BuyerId);
        builder.HasIndex(n => n.ProviderMessageSid);
        builder.HasIndex(n => n.ResendIdempotencyKey);
    }
}
