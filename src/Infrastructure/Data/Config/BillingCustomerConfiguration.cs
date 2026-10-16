using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerConfiguration : IEntityTypeConfiguration<BillingCustomer>
{
    public void Configure(EntityTypeBuilder<BillingCustomer> builder)
    {
        // The primary key is the claim: a second insert for the same buyer is refused by the store.
        builder.HasKey(c => c.BuyerId);

        builder.Property(c => c.BuyerId)
            .HasMaxLength(256);

        builder.Property(c => c.CustomerReference)
            .IsRequired()
            .HasMaxLength(64);
    }
}
