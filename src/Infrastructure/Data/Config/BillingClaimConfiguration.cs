using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingClaimConfiguration : IEntityTypeConfiguration<BillingClaim>
{
    public void Configure(EntityTypeBuilder<BillingClaim> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64).ValueGeneratedNever();
        builder.Property(x => x.Scope).HasMaxLength(64).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ProductHandle).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(80).IsRequired();
        builder.Property(x => x.State).HasMaxLength(16).IsRequired().IsConcurrencyToken();
        builder.HasIndex(x => new { x.Scope, x.UserId });
    }
}
