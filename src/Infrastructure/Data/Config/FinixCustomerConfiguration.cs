using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class FinixCustomerConfiguration : IEntityTypeConfiguration<FinixCustomer>
{
    public void Configure(EntityTypeBuilder<FinixCustomer> builder)
    {
        builder.ToTable("FinixCustomers");

        builder.Property(customer => customer.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(customer => customer.IdentityId)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(customer => customer.OwnerId)
            .IsUnique();
    }
}
