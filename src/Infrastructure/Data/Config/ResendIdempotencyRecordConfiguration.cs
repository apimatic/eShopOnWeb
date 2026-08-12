using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ResendIdempotencyRecordConfiguration : IEntityTypeConfiguration<ResendIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<ResendIdempotencyRecord> builder)
    {
        builder.ToTable("ResendIdempotencyRecords");

        builder.Property(r => r.Key)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        // The key is the idempotency guarantee: one key, one resend.
        builder.HasIndex(r => r.Key).IsUnique();
    }
}
