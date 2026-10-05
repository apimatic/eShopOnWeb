using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerConfiguration : IEntityTypeConfiguration<BillingCustomer>
{
    public void Configure(EntityTypeBuilder<BillingCustomer> builder)
    {
        // The primary key is the claim: a second row for the same user is refused by the store.
        builder.HasKey(c => c.UserId);

        builder.Property(c => c.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(c => c.BillingReference)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(c => c.ClaimToken)
            .IsConcurrencyToken();

        builder.Ignore(c => c.IsLinked);
    }
}
