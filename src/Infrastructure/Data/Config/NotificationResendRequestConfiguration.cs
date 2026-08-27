using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class NotificationResendRequestConfiguration : IEntityTypeConfiguration<NotificationResendRequest>
{
    public void Configure(EntityTypeBuilder<NotificationResendRequest> builder)
    {
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.SourceNotificationId, x.IdempotencyKey }).IsUnique();
        builder.HasOne<OrderNotification>().WithMany().HasForeignKey(x => x.SourceNotificationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderNotification>().WithMany().HasForeignKey(x => x.ResultNotificationId).OnDelete(DeleteBehavior.Restrict);
    }
}
