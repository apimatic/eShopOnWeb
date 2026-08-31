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
            .HasMaxLength(64);

        builder.HasIndex(i => i.ProviderInvoiceId)
            .IsUnique();

        builder.Property(i => i.InvoiceNumber)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(i => i.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(i => i.TotalAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(i => i.CustomerName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.CustomerEmail)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.PaymentLink)
            .HasMaxLength(2048);

        builder.Property(i => i.ProviderStatus)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(i => i.LifecycleState)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);
    }
}
