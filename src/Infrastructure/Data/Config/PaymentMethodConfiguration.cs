using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.Property<int>("BuyerId");
        builder.Property(x => x.Alias).HasMaxLength(64);
        builder.Property(x => x.CardId).HasMaxLength(255);
        builder.Property(x => x.Last4).HasMaxLength(4);
        builder.Property(x => x.Brand).HasMaxLength(32);
        builder.Property(x => x.Expiry).HasMaxLength(7);
        builder.Property(x => x.PayPalCustomerId).HasMaxLength(64);
        builder.Property(x => x.OperationKey).IsRequired().HasMaxLength(108);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex("BuyerId", nameof(PaymentMethod.OperationKey)).IsUnique();
    }
}
