using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationResendRequestConfiguration : IEntityTypeConfiguration<NotificationResendRequest>
{
    public void Configure(EntityTypeBuilder<NotificationResendRequest> builder)
    {
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => new { x.OriginalNotificationId, x.IdempotencyKey }).IsUnique();
    }
}
