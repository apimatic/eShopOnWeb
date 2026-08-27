using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ResendIdempotencyRecordConfiguration : IEntityTypeConfiguration<ResendIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<ResendIdempotencyRecord> builder)
    {
        builder.ToTable("ResendIdempotencyRecords");

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => new { r.OriginalNotificationId, r.IdempotencyKey })
            .IsUnique();
    }
}
