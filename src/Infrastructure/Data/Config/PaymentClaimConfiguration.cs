using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentClaimConfiguration : IEntityTypeConfiguration<PaymentClaim>
{
    public void Configure(EntityTypeBuilder<PaymentClaim> builder)
    {
        builder.HasKey(x => new { x.OrderId, x.Operation });
        builder.Property(x => x.Operation).IsRequired().HasMaxLength(32);
    }
}
