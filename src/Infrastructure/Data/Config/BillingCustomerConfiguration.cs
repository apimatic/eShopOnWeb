using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerConfiguration : IEntityTypeConfiguration<BillingCustomer>
{
    public void Configure(EntityTypeBuilder<BillingCustomer> builder)
    {
        // The primary key is the claim: the store refuses a second customer row for the same buyer.
        builder.HasKey(c => c.BuyerId);

        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.CustomerReference)
            .IsRequired()
            .HasMaxLength(300);

        builder.HasIndex(c => c.CustomerReference)
            .IsUnique();
    }
}
