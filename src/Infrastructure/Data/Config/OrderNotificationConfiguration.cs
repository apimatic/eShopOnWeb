using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Destination).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(34);
        builder.Property(x => x.ProviderStatus).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProviderErrorMessage).HasMaxLength(512);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200);

        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.ProviderMessageSid);
        builder.HasIndex(x => new { x.OriginalNotificationId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("[OriginalNotificationId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL");
    }
}
