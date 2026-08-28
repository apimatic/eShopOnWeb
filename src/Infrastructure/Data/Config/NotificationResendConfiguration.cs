using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationResendConfiguration : IEntityTypeConfiguration<NotificationResend>
{
    public void Configure(EntityTypeBuilder<NotificationResend> builder)
    {
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => new { x.SourceNotificationId, x.IdempotencyKey }).IsUnique();
        builder.HasOne<OrderNotification>().WithMany().HasForeignKey(x => x.SourceNotificationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderNotification>().WithMany().HasForeignKey(x => x.NotificationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
