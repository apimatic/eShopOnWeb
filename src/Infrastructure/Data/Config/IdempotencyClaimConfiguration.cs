using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class IdempotencyClaimConfiguration : IEntityTypeConfiguration<IdempotencyClaim>
{
    public void Configure(EntityTypeBuilder<IdempotencyClaim> builder)
    {
        // The claim string IS the primary key: a second insert of the same key fails the store's
        // primary-key uniqueness at save time, which is what rejects a duplicate request.
        builder.HasKey(c => c.Key);
        builder.Property(c => c.Key).HasMaxLength(256);
    }
}
