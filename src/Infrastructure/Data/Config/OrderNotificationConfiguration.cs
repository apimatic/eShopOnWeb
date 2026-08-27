using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(1600);
        builder.Property(x => x.ProviderSid).HasMaxLength(64);
        builder.Property(x => x.ProviderStatus).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.BuyerId);
        builder.HasIndex(x => x.ProviderSid).IsUnique().HasFilter("[ProviderSid] IS NOT NULL");
    }
}
