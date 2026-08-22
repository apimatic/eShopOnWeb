using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ResendIdempotencyRecordConfiguration : IEntityTypeConfiguration<ResendIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<ResendIdempotencyRecord> builder)
    {
        builder.ToTable("NotificationResendKeys");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => new { r.SourceNotificationId, r.IdempotencyKey }).IsUnique();
    }
}
