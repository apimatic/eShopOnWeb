using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ProviderResponseRecordConfiguration : IEntityTypeConfiguration<ProviderResponseRecord>
{
    public void Configure(EntityTypeBuilder<ProviderResponseRecord> builder)
    {
        // Stored verbatim (unbounded) so fields the provider adds later are never lost.
        builder.Property(r => r.Body).IsRequired();
    }
}
