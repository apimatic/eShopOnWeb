using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.ToTable("OrderNotifications");
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.LocalOutcome).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(64);
        builder.Property(x => x.ProviderStatus).HasMaxLength(64);
        builder.Property(x => x.ProviderFrom).HasMaxLength(32);
        builder.Property(x => x.ProviderErrorMessage).HasMaxLength(2048);
        builder.Property(x => x.ProviderDateCreated).HasMaxLength(128);
        builder.Property(x => x.ProviderDateUpdated).HasMaxLength(128);
        builder.Property(x => x.ProviderDateSent).HasMaxLength(128);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128);
        builder.HasIndex(x => x.ProviderMessageId).IsUnique().HasFilter("[ProviderMessageId] IS NOT NULL");
        builder.HasIndex(x => new { x.OriginalNotificationId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("[OriginalNotificationId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL");
        builder.HasIndex(x => new { x.OrderId, x.CreatedAt });
        builder.HasIndex(x => x.CancellationPending);
    }
}
