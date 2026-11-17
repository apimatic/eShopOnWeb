using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentOperationClaimConfiguration : IEntityTypeConfiguration<PaymentOperationClaim>
{
    public void Configure(EntityTypeBuilder<PaymentOperationClaim> builder)
    {
        builder.HasKey(c => c.Key);

        builder.Property(c => c.Key)
            .HasMaxLength(256);

        builder.Property(c => c.Operation)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.ReferenceId)
            .HasMaxLength(256);

        builder.Property(c => c.ResultJson)
            .HasMaxLength(4000);
    }
}
