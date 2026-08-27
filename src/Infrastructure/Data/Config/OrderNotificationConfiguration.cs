using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(64);
        builder.Property(x => x.ProviderStatus).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResendIdempotencyKey).HasMaxLength(128);

        builder.HasIndex(x => x.ProviderMessageSid).IsUnique().HasFilter("[ProviderMessageSid] IS NOT NULL");
        builder.HasIndex(x => new { x.OriginalNotificationId, x.ResendIdempotencyKey })
            .IsUnique()
            .HasFilter("[OriginalNotificationId] IS NOT NULL AND [ResendIdempotencyKey] IS NOT NULL");

        builder.HasOne<Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<RegisteredContactNumber>()
            .WithMany()
            .HasForeignKey(x => x.ContactNumberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
