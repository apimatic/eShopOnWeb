using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class InvoiceRecordConfiguration : IEntityTypeConfiguration<InvoiceRecord>
{
    public void Configure(EntityTypeBuilder<InvoiceRecord> builder)
    {
        builder.Property(i => i.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.ProviderInvoiceId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(i => i.Status)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(i => i.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(i => i.CustomerName)
            .HasMaxLength(256);

        builder.Property(i => i.CustomerEmail)
            .HasMaxLength(256);

        builder.Property(i => i.Description)
            .HasMaxLength(256);

        builder.Property(i => i.PaymentLink)
            .HasMaxLength(2048);

        builder.Property(i => i.Amount)
            .HasColumnType("decimal(18,2)");

        builder.HasIndex(i => i.ProviderInvoiceId);
        builder.HasIndex(i => i.BuyerId);
    }
}
