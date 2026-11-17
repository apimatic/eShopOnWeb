using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ActionClaimConfiguration : IEntityTypeConfiguration<ActionClaim>
{
    public void Configure(EntityTypeBuilder<ActionClaim> builder)
    {
        builder.HasKey(c => new { c.OrderId, c.Action });
        builder.Property(c => c.Action).HasMaxLength(32);
    }
}
