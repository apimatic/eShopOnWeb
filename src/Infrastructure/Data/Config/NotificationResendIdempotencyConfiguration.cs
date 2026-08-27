using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationResendIdempotencyConfiguration : IEntityTypeConfiguration<NotificationResendIdempotency>
{
    public void Configure(EntityTypeBuilder<NotificationResendIdempotency> builder)
    {
        builder.ToTable("NotificationResendIdempotency");

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => new { r.SourceNotificationId, r.IdempotencyKey })
            .IsUnique();
    }
}
