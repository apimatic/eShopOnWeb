using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.HasIndex(x => new { x.OwnerId, x.PayPalTokenId }).IsUnique();
        builder.Property(x => x.OwnerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PayPalTokenId).HasMaxLength(128);
        builder.Property(x => x.PayPalCustomerId).HasMaxLength(128);
        builder.Ignore(x => x.IsActive);
    }
}
