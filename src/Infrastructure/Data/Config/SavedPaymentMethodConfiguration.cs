using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");
        builder.Property(m => m.BuyerIdentityGuid).HasMaxLength(256).IsRequired();
        builder.Property(m => m.PayPalVaultId).HasMaxLength(100).IsRequired();
        builder.Property(m => m.PayPalCustomerId).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Last4).HasMaxLength(4).IsRequired();
        builder.Property(m => m.Brand).HasMaxLength(50).IsRequired();
    }
}
