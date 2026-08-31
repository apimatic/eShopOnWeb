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
            .HasMaxLength(256);

        // One bill per provider identifier — callers address bills by it.
        builder.HasIndex(i => i.ProviderInvoiceId).IsUnique();

        builder.Property(i => i.MerchantReference)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(i => i.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(i => i.CustomerName)
            .HasMaxLength(256);

        builder.Property(i => i.CustomerEmail)
            .HasMaxLength(256);

        // Persist the lifecycle stage by name so the stored value stays readable and stable.
        builder.Property(i => i.State)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(i => i.ProviderStatus)
            .HasMaxLength(64);

        builder.Property(i => i.PaymentLink)
            .HasMaxLength(2048);
    }
}
