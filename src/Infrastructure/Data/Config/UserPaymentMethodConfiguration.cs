using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserPaymentMethodConfiguration : IEntityTypeConfiguration<UserPaymentMethod>
{
    public void Configure(EntityTypeBuilder<UserPaymentMethod> builder)
    {
        builder.Property(p => p.UserId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PaymentTokenId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.PayPalCustomerId).HasMaxLength(256);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.Property(p => p.Brand).HasMaxLength(50);
        builder.Property(p => p.Expiry).HasMaxLength(7);
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
