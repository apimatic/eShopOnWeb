using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ResendIdempotencyConfiguration : IEntityTypeConfiguration<ResendIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<ResendIdempotencyRecord> builder)
    {
        builder.ToTable("ResendIdempotency");

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(r => new { r.IdempotencyKey, r.SourceNotificationId }).IsUnique();
    }
}
