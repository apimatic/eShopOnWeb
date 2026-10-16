using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class BillingClaimConfiguration : IEntityTypeConfiguration<BillingClaim>
{
    public void Configure(EntityTypeBuilder<BillingClaim> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(80);
        builder.Property(x => x.Scope).HasMaxLength(64).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ProductHandle).HasMaxLength(255);
        builder.HasIndex(x => new { x.Scope, x.RemoteId });
    }
}
