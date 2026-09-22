using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SmsIdempotencyKeyConfiguration : IEntityTypeConfiguration<SmsIdempotencyKey>
{
    public void Configure(EntityTypeBuilder<SmsIdempotencyKey> builder)
    {
        // The caller-supplied key is the primary key, so a duplicate insert is rejected by the
        // store's PK constraint (SQL Server; and the EF in-memory provider rejects a duplicate PK
        // at SaveChanges too) — the claim, not a check-then-act read.
        builder.HasKey(k => k.Key);

        builder.Property(k => k.Key)
            .HasMaxLength(128)
            .ValueGeneratedNever();
    }
}
