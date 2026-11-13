using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerLinkConfiguration : IEntityTypeConfiguration<BillingCustomerLink>
{
    public void Configure(EntityTypeBuilder<BillingCustomerLink> builder)
    {
        // The primary key IS the duplicate guard: a second insert for the same user is refused by the store.
        builder.HasKey(l => l.CustomerReference);

        builder.Property(l => l.CustomerReference)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(l => l.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(l => l.ClaimToken)
            .IsRequired()
            .IsConcurrencyToken();

        builder.Ignore(l => l.IsLinked);
    }
}
