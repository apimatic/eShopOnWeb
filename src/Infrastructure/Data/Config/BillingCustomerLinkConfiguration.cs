using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerLinkConfiguration : IEntityTypeConfiguration<BillingCustomerLink>
{
    public void Configure(EntityTypeBuilder<BillingCustomerLink> builder)
    {
        // Natural key, never store-generated: inserting it is the claim.
        builder.HasKey(l => l.UserKey);
        builder.Property(l => l.UserKey)
            .HasMaxLength(256)
            .ValueGeneratedNever();

        builder.Property(l => l.CustomerReference)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(l => l.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(l => l.Version)
            .IsConcurrencyToken();
    }
}
