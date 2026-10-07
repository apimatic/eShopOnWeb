using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentProviderRecordEntryConfiguration : IEntityTypeConfiguration<PaymentProviderRecordEntry>
{
    public void Configure(EntityTypeBuilder<PaymentProviderRecordEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Provider).HasMaxLength(32).IsRequired();
        builder.Property(e => e.Operation).HasMaxLength(32).IsRequired();
        builder.Property(e => e.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(e => e.MerchantReference).HasMaxLength(80).IsRequired();
        builder.Property(e => e.TransportError).HasMaxLength(1024);
        // ResponseBody is unbounded: the provider's response is kept verbatim, whatever fields it grows.

        builder.HasIndex(e => new { e.OrderId, e.RecordedAt });
    }
}
