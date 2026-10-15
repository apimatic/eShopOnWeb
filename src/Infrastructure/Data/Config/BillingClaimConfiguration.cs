using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.Infrastructure.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingClaimConfiguration : IEntityTypeConfiguration<BillingClaim>
{
    public void Configure(EntityTypeBuilder<BillingClaim> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64).ValueGeneratedNever();
        builder.Property(x => x.Site).HasMaxLength(64).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ProductHandle).HasMaxLength(255);
        builder.Property(x => x.State).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.Site, x.UserId });
        builder.HasIndex(x => new { x.State, x.UpdatedUtc });
    }
}
