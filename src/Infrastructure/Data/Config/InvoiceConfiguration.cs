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
            .HasMaxLength(128);

        builder.HasIndex(i => i.ProviderInvoiceId)
            .IsUnique();

        builder.HasIndex(i => i.BuyerId);

        builder.Property(i => i.CurrencyCode)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(i => i.Status)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(i => i.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(i => i.CustomerName)
            .HasMaxLength(256);

        builder.Property(i => i.CustomerEmail)
            .HasMaxLength(256);
    }
}
