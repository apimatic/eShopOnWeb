using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingAccountConfiguration : IEntityTypeConfiguration<BillingAccount>
{
    public void Configure(EntityTypeBuilder<BillingAccount> builder)
    {
        builder.ToTable("BillingAccounts");
        builder.Property(a => a.UserId).HasMaxLength(450).IsRequired();
        builder.Property(a => a.CustomerReference).HasMaxLength(100).IsRequired();
        builder.HasIndex(a => a.UserId).IsUnique();
        builder.HasIndex(a => a.CustomerReference).IsUnique();
    }
}