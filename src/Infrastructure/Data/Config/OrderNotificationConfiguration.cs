using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProviderStatus).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(34);
        builder.Property(x => x.IdempotencyKeyHash).HasMaxLength(64);
        builder.Property(x => x.Content).HasMaxLength(1600);

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ContactNumber>()
            .WithMany()
            .HasForeignKey(x => x.ContactNumberId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ProviderMessageSid).IsUnique().HasFilter("[ProviderMessageSid] IS NOT NULL");
        builder.HasIndex(x => new { x.ResendOfNotificationId, x.IdempotencyKeyHash })
            .IsUnique()
            .HasFilter("[ResendOfNotificationId] IS NOT NULL AND [IdempotencyKeyHash] IS NOT NULL");
    }
}
