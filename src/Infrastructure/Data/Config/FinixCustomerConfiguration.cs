using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class FinixCustomerConfiguration : IEntityTypeConfiguration<FinixCustomer>
{
    public void Configure(EntityTypeBuilder<FinixCustomer> builder)
    {
        builder.Property(c => c.UserId).HasMaxLength(256).IsRequired();
        builder.Property(c => c.FinixIdentityId).HasMaxLength(64).IsRequired();

        builder.HasIndex(c => c.UserId).IsUnique();
    }
}
