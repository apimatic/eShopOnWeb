using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Body).HasMaxLength(1600);
        builder.Property(x => x.ProviderMessageSid).HasMaxLength(34);
        builder.Property(x => x.ProviderStatus).IsRequired().HasMaxLength(40);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(40);
        builder.HasIndex(x => x.ProviderMessageSid).IsUnique();
        builder.HasIndex(x => new { x.OrderId, x.CreatedAt });
        builder.HasOne<ContactNumber>().WithMany().HasForeignKey(x => x.ContactNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order>()
            .WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class NotificationResendConfiguration : IEntityTypeConfiguration<NotificationResend>
{
    public void Configure(EntityTypeBuilder<NotificationResend> builder)
    {
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.HasIndex(x => new { x.SourceNotificationId, x.IdempotencyKey }).IsUnique();
        builder.HasOne<OrderNotification>().WithMany().HasForeignKey(x => x.SourceNotificationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Notification).WithMany().HasForeignKey(x => x.NotificationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
