using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.ToTable("OrderNotifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Content).HasMaxLength(1600);
        builder.Property(x => x.ProviderSid).HasMaxLength(64);
        builder.Property(x => x.ProviderFrom).HasMaxLength(32);
        builder.Property(x => x.ProviderStatus).HasMaxLength(64);
        builder.Property(x => x.ProviderErrorMessage).HasMaxLength(2048);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ContactNumber>().WithMany().HasForeignKey(x => x.ContactNumberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.ProviderSid).IsUnique().HasFilter("[ProviderSid] IS NOT NULL");
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => new { x.ResendOfNotificationId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("[ResendOfNotificationId] IS NOT NULL AND [IdempotencyKey] IS NOT NULL");
    }
}
