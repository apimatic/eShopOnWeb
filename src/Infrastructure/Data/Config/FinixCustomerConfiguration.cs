using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class FinixCustomerConfiguration : IEntityTypeConfiguration<FinixCustomer>
{
    public void Configure(EntityTypeBuilder<FinixCustomer> builder)
    {
        builder.HasKey(customer => customer.Id);

        builder.Property(customer => customer.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(customer => customer.FinixIdentityId)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(customer => customer.BuyerId)
            .IsUnique();
    }
}
