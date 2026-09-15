using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Destination).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(64);
        builder.Property(x => x.ProviderStatus).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProviderErrorMessage).HasMaxLength(1024);
        builder.Property(x => x.RefreshDiagnostic).HasMaxLength(256);
        builder.Property(x => x.ResendIdempotencyKey).HasMaxLength(128);
        builder.HasIndex(x => x.ProviderMessageId).IsUnique().HasFilter("[ProviderMessageId] IS NOT NULL");
        builder.HasIndex(x => new { x.OriginalNotificationId, x.ResendIdempotencyKey })
            .IsUnique()
            .HasFilter("[OriginalNotificationId] IS NOT NULL AND [ResendIdempotencyKey] IS NOT NULL");
        builder.HasIndex(x => new { x.OrderId, x.CreatedAt });
        builder.HasIndex(x => new { x.CancellationRequested, x.ScheduledFor });
    }
}
