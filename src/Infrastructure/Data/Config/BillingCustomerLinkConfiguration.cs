using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerLinkConfiguration : IEntityTypeConfiguration<BillingCustomerLink>
{
    public void Configure(EntityTypeBuilder<BillingCustomerLink> builder)
    {
        builder.ToTable("BillingCustomerLinks");

        // The primary key is the claim: a second row for the same user is refused by the store.
        builder.HasKey(l => l.UserId);

        builder.Property(l => l.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(l => l.Status)
            .IsRequired();

        builder.Property(l => l.ClaimToken)
            .IsConcurrencyToken();
    }
}
