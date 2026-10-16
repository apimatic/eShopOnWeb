using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentClaimConfiguration : IEntityTypeConfiguration<SubscriptionEnrollmentClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollmentClaim> builder)
    {
        builder.ToTable("SubscriptionEnrollmentClaims");
        builder.HasKey(claim => claim.Reference);
        builder.Property(claim => claim.Reference).HasMaxLength(450);
        builder.Property(claim => claim.UserId).IsRequired().HasMaxLength(450);
        builder.Property(claim => claim.ProductHandle).IsRequired().HasMaxLength(200);
        builder.Property(claim => claim.State).IsRequired().HasMaxLength(32);
    }
}
