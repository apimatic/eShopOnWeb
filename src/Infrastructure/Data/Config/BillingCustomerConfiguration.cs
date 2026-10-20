using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerConfiguration : IEntityTypeConfiguration<BillingCustomer>
{
    public void Configure(EntityTypeBuilder<BillingCustomer> builder)
    {
        // The primary key is the duplicate guard: a second claim for the same user is refused by the store.
        builder.HasKey(c => c.CustomerReference);

        builder.Property(c => c.CustomerReference)
            .HasMaxLength(256);

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(c => c.Version)
            .IsConcurrencyToken();
    }
}
