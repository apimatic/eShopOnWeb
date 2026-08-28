using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.BuyerId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Body)
            .HasMaxLength(1600);

        builder.Property(x => x.ProviderMessageSid)
            .HasMaxLength(34);

        builder.Property(x => x.ProviderStatus)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.IdempotencyKey)
            .HasMaxLength(128);

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ContactNumber>()
            .WithMany()
            .HasForeignKey(x => x.ContactNumberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<OrderNotification>()
            .WithMany()
            .HasForeignKey(x => x.ResendOfNotificationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ProviderMessageSid)
            .IsUnique()
            .HasFilter("[ProviderMessageSid] IS NOT NULL");

        builder.HasIndex(x => new { x.ResendOfNotificationId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("[ResendOfNotificationId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL");
    }
}
