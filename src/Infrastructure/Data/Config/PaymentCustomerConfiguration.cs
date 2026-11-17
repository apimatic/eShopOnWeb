using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentCustomerConfiguration : IEntityTypeConfiguration<PaymentCustomer>
{
    public void Configure(EntityTypeBuilder<PaymentCustomer> builder)
    {
        builder.Property(customer => customer.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(customer => customer.FinixIdentityId)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(customer => customer.BuyerId)
            .IsUnique();
    }
}
