using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerLinkConfiguration : IEntityTypeConfiguration<BillingCustomerLink>
{
    public void Configure(EntityTypeBuilder<BillingCustomerLink> builder)
    {
        // The primary key IS the claim: a second insert for the same user is refused by the store
        // (SQL Server and the EF in-memory provider both enforce primary keys).
        builder.HasKey(l => l.CustomerReference);

        builder.Property(l => l.CustomerReference)
            .IsRequired()
            .HasMaxLength(256)
            .ValueGeneratedNever();

        builder.Property(l => l.Status)
            .IsRequired();

        builder.Property(l => l.Version)
            .IsConcurrencyToken();
    }
}
