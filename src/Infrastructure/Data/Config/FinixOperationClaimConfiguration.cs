using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class FinixOperationClaimConfiguration : IEntityTypeConfiguration<FinixOperationClaim>
{
    public void Configure(EntityTypeBuilder<FinixOperationClaim> builder)
    {
        builder.HasKey(c => c.ClaimKey);

        builder.Property(c => c.ClaimKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.Operation)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.ReferenceId).HasMaxLength(64);
        builder.Property(c => c.Status).IsRequired().HasMaxLength(32);
        builder.Property(c => c.Detail).HasMaxLength(500);
    }
}
