using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class IdempotencyClaimConfiguration : IEntityTypeConfiguration<IdempotencyClaim>
{
    public void Configure(EntityTypeBuilder<IdempotencyClaim> builder)
    {
        // The primary key is the claim string itself — a duplicate insert is refused by the store,
        // which is the concurrency guard for double-clicked writes.
        builder.HasKey(c => c.Key);
        builder.Property(c => c.Key).HasMaxLength(200);
    }
}
