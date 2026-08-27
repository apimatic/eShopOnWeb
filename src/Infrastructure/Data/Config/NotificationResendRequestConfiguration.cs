using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationResendRequestConfiguration : IEntityTypeConfiguration<NotificationResendRequest>
{
    public void Configure(EntityTypeBuilder<NotificationResendRequest> builder)
    {
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.HasIndex(x => new { x.SourceNotificationId, x.IdempotencyKey }).IsUnique();
        builder.HasOne<OrderNotification>()
            .WithMany()
            .HasForeignKey(x => x.SourceNotificationId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrderNotification>()
            .WithMany()
            .HasForeignKey(x => x.NotificationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
