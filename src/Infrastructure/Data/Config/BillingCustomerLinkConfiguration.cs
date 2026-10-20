using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerLinkConfiguration : IEntityTypeConfiguration<BillingCustomerLink>
{
    public void Configure(EntityTypeBuilder<BillingCustomerLink> builder)
    {
        builder.ToTable("BillingCustomerLinks");

        // The customer reference is the primary key: inserting a second claim for the same
        // reference is refused by the store, which is what stops a duplicate customer create.
        builder.HasKey(l => l.Key);
        builder.Property(l => l.Key)
            .HasMaxLength(BillingClaimColumns.KeyMaxLength)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(l => l.State)
            .HasConversion<string>()
            .HasMaxLength(BillingClaimColumns.StateMaxLength)
            .IsRequired();

        builder.Property(l => l.Version).IsConcurrencyToken();
    }
}
