using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class IdempotencyClaimConfiguration : IEntityTypeConfiguration<IdempotencyClaim>
{
    public void Configure(EntityTypeBuilder<IdempotencyClaim> builder)
    {
        builder.HasKey(c => c.ClaimKey);

        builder.Property(c => c.ClaimKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.Operation)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.OrderId).HasMaxLength(64);
    }
}
