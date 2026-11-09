using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ShopperStaxCustomerConfiguration : IEntityTypeConfiguration<ShopperStaxCustomer>
{
    public void Configure(EntityTypeBuilder<ShopperStaxCustomer> builder)
    {
        builder.Property(s => s.Email).IsRequired().HasMaxLength(256);
        builder.Property(s => s.StaxCustomerId).IsRequired().HasMaxLength(64);
        builder.HasIndex(s => s.Email).IsUnique();
    }
}
