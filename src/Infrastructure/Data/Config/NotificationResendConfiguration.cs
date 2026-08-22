using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationResendConfiguration : IEntityTypeConfiguration<NotificationResend>
{
    public void Configure(EntityTypeBuilder<NotificationResend> builder)
    {
        builder.ToTable("NotificationResends");

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => new { r.OriginalNotificationId, r.IdempotencyKey })
            .IsUnique();
    }
}
