using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        // The primary key is the duplicate guard: a second claim for the same user is refused by the store.
        builder.HasKey(l => l.UserName);

        builder.Property(l => l.UserName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(l => l.Status)
            .IsRequired();

        builder.Property(l => l.LeaseId)
            .IsConcurrencyToken();
    }
}
