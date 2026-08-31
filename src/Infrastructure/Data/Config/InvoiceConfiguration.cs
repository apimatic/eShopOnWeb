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

        builder.Property(i => i.InvoiceNumber)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(i => i.ProviderInvoiceId)
            .HasMaxLength(64);

        builder.Property(i => i.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(i => i.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(i => i.CustomerName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.CustomerEmail)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(i => i.ProviderInvoiceId);
        builder.HasIndex(i => i.BuyerId);
    }
}
