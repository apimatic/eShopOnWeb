using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.Property(i => i.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.ProviderInvoiceId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(i => i.MerchantCustomerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.Description)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(i => i.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(i => i.ProviderStatus)
            .HasMaxLength(50);

        // Store the eShop lifecycle as its readable name rather than an opaque integer.
        builder.Property(i => i.Status)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(20);

        builder.OwnsOne(i => i.Customer, c =>
        {
            c.WithOwner();

            c.Property(p => p.Name)
                .HasColumnName("CustomerName")
                .HasMaxLength(100)
                .IsRequired();

            c.Property(p => p.Email)
                .HasColumnName("CustomerEmail")
                .HasMaxLength(256)
                .IsRequired();
        });

        builder.Navigation(i => i.Customer).IsRequired();
    }
}
