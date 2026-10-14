using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerConfiguration : IEntityTypeConfiguration<BillingCustomer>
{
    public void Configure(EntityTypeBuilder<BillingCustomer> builder)
    {
        // The primary key is the duplicate-customer guard: a second claim for the same user is refused on insert.
        builder.HasKey(c => c.UserId);

        builder.Property(c => c.UserId)
            .HasMaxLength(450)
            .ValueGeneratedNever();

        builder.Property(c => c.Reference)
            .IsRequired()
            .HasMaxLength(255);

        builder.Ignore(c => c.IsProvisioned);
    }
}
