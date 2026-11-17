using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(s => s.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(s => s.FinixPaymentInstrumentId).IsRequired().HasMaxLength(64);
        builder.Property(s => s.Brand).HasMaxLength(64);
        builder.Property(s => s.Last4).HasMaxLength(4);
        builder.Property(s => s.Name).HasMaxLength(256);

        builder.HasIndex(s => s.BuyerId);
        builder.HasIndex(s => s.FinixPaymentInstrumentId).IsUnique();
    }
}

public class FinixCustomerConfiguration : IEntityTypeConfiguration<FinixCustomer>
{
    public void Configure(EntityTypeBuilder<FinixCustomer> builder)
    {
        builder.ToTable("FinixCustomers");

        builder.Property(c => c.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.FinixIdentityId).IsRequired().HasMaxLength(64);

        builder.HasIndex(c => c.BuyerId).IsUnique();
    }
}
