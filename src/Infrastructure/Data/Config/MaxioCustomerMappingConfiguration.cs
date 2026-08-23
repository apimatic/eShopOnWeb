using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerMappingConfiguration : IEntityTypeConfiguration<MaxioCustomerMapping>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerMapping> builder)
    {
        builder.ToTable("MaxioCustomers");
        builder.Property(x => x.ApplicationUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.MaxioCustomerReference).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.ApplicationUserId).IsUnique();
        builder.HasIndex(x => x.MaxioCustomerReference).IsUnique();
        builder.HasIndex(x => x.MaxioCustomerId).IsUnique().HasFilter("[MaxioCustomerId] IS NOT NULL");
    }
}
