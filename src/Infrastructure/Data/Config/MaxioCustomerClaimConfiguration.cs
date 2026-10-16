using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerClaimConfiguration : IEntityTypeConfiguration<MaxioCustomerClaim>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerClaim> builder)
    {
        builder.ToTable("MaxioCustomerClaims");
        builder.HasKey(claim => claim.UserId);
        builder.Property(claim => claim.UserId).HasMaxLength(450);
        builder.Property(claim => claim.State).IsRequired().HasMaxLength(32);
    }
}
