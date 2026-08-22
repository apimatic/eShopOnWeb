using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationResendRecordConfiguration : IEntityTypeConfiguration<NotificationResendRecord>
{
    public void Configure(EntityTypeBuilder<NotificationResendRecord> builder)
    {
        builder.ToTable("NotificationResendRecords");

        builder.HasKey(record => record.Id);

        builder.Property(record => record.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(record => new { record.SourceNotificationId, record.IdempotencyKey })
            .IsUnique();
    }
}
