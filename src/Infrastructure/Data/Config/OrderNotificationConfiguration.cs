using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Body).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(64);
        builder.Property(x => x.ProviderStatus).IsRequired().HasMaxLength(32);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128);
        builder.HasIndex(x => x.ProviderMessageSid).IsUnique().HasFilter("[ProviderMessageSid] IS NOT NULL");
        builder.HasIndex(x => new { x.ParentNotificationId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("[ParentNotificationId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL");
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ContactNumber>()
            .WithMany()
            .HasForeignKey(x => x.ContactNumberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
