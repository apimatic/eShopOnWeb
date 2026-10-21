using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerLinkConfiguration : IEntityTypeConfiguration<BillingCustomerLink>
{
    public void Configure(EntityTypeBuilder<BillingCustomerLink> builder)
    {
        // The primary key is the duplicate guard: a second insert for the same user is refused by the store.
        builder.HasKey(l => l.UserId);

        builder.Property(l => l.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(l => l.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(l => l.ConcurrencyStamp)
            .IsConcurrencyToken();

        builder.Ignore(l => l.IsCompleted);
    }
}
