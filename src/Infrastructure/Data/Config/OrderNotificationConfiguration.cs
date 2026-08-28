using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.Body)
            .HasMaxLength(1600);

        builder.Property(x => x.ProviderMessageSid)
            .HasMaxLength(34);

        builder.Property(x => x.ProviderStatus)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(x => x.ResendIdempotencyKey)
            .HasMaxLength(128);

        builder.HasIndex(x => x.ProviderMessageSid)
            .IsUnique()
            .HasFilter("[ProviderMessageSid] IS NOT NULL");

        builder.HasIndex(x => x.ResendIdempotencyKey)
            .IsUnique()
            .HasFilter("[ResendIdempotencyKey] IS NOT NULL");

        builder.HasIndex(x => new { x.OrderId, x.CreatedAt });
        builder.HasIndex(x => new { x.BuyerId, x.CreatedAt });

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ContactNumber>()
            .WithMany()
            .HasForeignKey(x => x.ContactNumberId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<OrderNotification>()
            .WithMany()
            .HasForeignKey(x => x.OriginalNotificationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
