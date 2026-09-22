using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ResendClaimConfiguration : IEntityTypeConfiguration<ResendClaim>
{
    public void Configure(EntityTypeBuilder<ResendClaim> builder)
    {
        // The idempotency key IS the primary key: a second insert under the same key is rejected by
        // the store (SQL Server and the EF in-memory provider both enforce PK uniqueness). That
        // rejection is what makes a repeated resend a no-op.
        builder.HasKey(c => c.IdempotencyKey);

        builder.Property(c => c.IdempotencyKey)
            .HasMaxLength(128)
            .ValueGeneratedNever();
    }
}
