using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentProviderResponseConfiguration : IEntityTypeConfiguration<PaymentProviderResponse>
{
    public void Configure(EntityTypeBuilder<PaymentProviderResponse> builder)
    {
        builder.Property(r => r.Operation).HasMaxLength(16).IsRequired();
        builder.Property(r => r.Note).HasMaxLength(512);
        // Body is unbounded on purpose: it is the provider's response verbatim, including fields added later.
        builder.HasIndex(r => r.OrderId);
    }
}
