using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationResendRequestConfiguration : IEntityTypeConfiguration<NotificationResendRequest>
{
    public void Configure(EntityTypeBuilder<NotificationResendRequest> builder)
    {
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.HasIndex(x => new { x.SourceNotificationId, x.IdempotencyKey }).IsUnique();
        builder.HasOne(x => x.ResultNotification)
            .WithMany()
            .HasForeignKey(x => x.ResultNotificationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
