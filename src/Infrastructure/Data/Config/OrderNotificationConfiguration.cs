using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderNotificationConfiguration : IEntityTypeConfiguration<OrderNotification>
{
    public void Configure(EntityTypeBuilder<OrderNotification> builder)
    {
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Content).HasMaxLength(1600);
        builder.Property(x => x.ProviderSid).HasMaxLength(64);
        builder.Property(x => x.ProviderStatus).HasMaxLength(64);
        builder.Property(x => x.Outcome).IsRequired().HasMaxLength(64);
        builder.HasIndex(x => x.ProviderSid).IsUnique().HasFilter("[ProviderSid] IS NOT NULL");
        builder.HasIndex(x => new { x.OrderId, x.CreatedAt });
        builder.HasIndex(x => new { x.CancellationRequested, x.ScheduledFor });
        builder.Ignore(x => x.IsContentDisposed);
        builder.Ignore(x => x.IsScheduled);
    }
}
