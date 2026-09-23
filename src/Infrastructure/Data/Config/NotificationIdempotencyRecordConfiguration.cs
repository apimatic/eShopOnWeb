using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationIdempotencyRecordConfiguration : IEntityTypeConfiguration<NotificationIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<NotificationIdempotencyRecord> builder)
    {
        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(200);

        // The claim that a resend under this key has already been made: a second insert is rejected here.
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();
    }
}
