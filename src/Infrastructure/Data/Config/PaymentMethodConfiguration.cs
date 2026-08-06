using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        // Only the PayPal vault token and safe descriptors are stored - never a PAN or CVV.
        builder.Property(p => p.CardId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(p => p.Alias).HasMaxLength(200);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.Property(p => p.Brand).HasMaxLength(60);
        builder.Property(p => p.Expiry).HasMaxLength(7);
    }
}
