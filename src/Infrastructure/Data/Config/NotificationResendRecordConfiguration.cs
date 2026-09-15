using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationResendRecordConfiguration : IEntityTypeConfiguration<NotificationResendRecord>
{
    public void Configure(EntityTypeBuilder<NotificationResendRecord> builder)
    {
        builder.ToTable("NotificationResendRecords");

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => new { r.NotificationId, r.IdempotencyKey }).IsUnique();
    }
}
