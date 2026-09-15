using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.DeliveryStatus).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(34);
        builder.Property(x => x.ProviderStatus).HasMaxLength(32);
        builder.Property(x => x.ProviderErrorMessage).HasMaxLength(1024);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200);
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.ProviderMessageSid).IsUnique().HasFilter("[ProviderMessageSid] IS NOT NULL");
        builder.HasIndex(x => new { x.SourceNotificationId, x.IdempotencyKey }).IsUnique()
            .HasFilter("[SourceNotificationId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL");
    }
}
