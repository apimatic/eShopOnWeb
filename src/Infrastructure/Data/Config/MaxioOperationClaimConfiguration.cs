using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioOperationClaimConfiguration : IEntityTypeConfiguration<MaxioOperationClaim>
{
    public void Configure(EntityTypeBuilder<MaxioOperationClaim> builder)
    {
        builder.HasKey(c => c.ClaimKey);
        builder.Property(c => c.ClaimKey).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Attempt).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Result).HasMaxLength(100);
    }
}
