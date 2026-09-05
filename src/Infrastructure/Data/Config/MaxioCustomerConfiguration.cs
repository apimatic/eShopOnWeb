using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerConfiguration : IEntityTypeConfiguration<MaxioCustomer>
{
    public void Configure(EntityTypeBuilder<MaxioCustomer> builder)
    {
        builder.ToTable("MaxioCustomers");
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.MaxioCustomerId).IsRequired();
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasIndex(x => x.MaxioCustomerId).IsUnique();
    }
}
