using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationIdempotencyRecordConfiguration : IEntityTypeConfiguration<NotificationIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<NotificationIdempotencyRecord> builder)
    {
        builder.ToTable("NotificationIdempotencyRecords");

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => new { r.SourceNotificationId, r.IdempotencyKey })
            .IsUnique();
    }
}
