using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerLinkConfiguration : IEntityTypeConfiguration<BillingCustomerLink>
{
    public void Configure(EntityTypeBuilder<BillingCustomerLink> builder)
    {
        // The primary key is the duplicate guard: a second link for the same user is refused on insert.
        builder.HasKey(l => l.UserName);

        builder.Property(l => l.UserName)
            .HasMaxLength(256);

        builder.Property(l => l.CustomerReference)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(l => l.Version)
            .IsConcurrencyToken();
    }
}
