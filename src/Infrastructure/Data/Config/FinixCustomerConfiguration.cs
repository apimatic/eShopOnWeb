using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class FinixCustomerConfiguration : IEntityTypeConfiguration<FinixCustomer>
{
    public void Configure(EntityTypeBuilder<FinixCustomer> builder)
    {
        builder.ToTable("FinixCustomers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.FinixIdentityId)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(c => c.OwnerId).IsUnique();
    }
}
