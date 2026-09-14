using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingCustomerConfiguration : IEntityTypeConfiguration<BillingCustomer>
{
    public void Configure(EntityTypeBuilder<BillingCustomer> builder)
    {
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).ValueGeneratedOnAdd();
        builder.Property(customer => customer.UserId).IsRequired().HasMaxLength(450);
        builder.Property(customer => customer.CustomerReference).IsRequired().HasMaxLength(500);
        builder.Property(customer => customer.MaxioCustomerId).IsRequired();
        builder.Property(customer => customer.CreatedAtUtc).IsRequired();
        builder.HasIndex(customer => customer.UserId).IsUnique();
        builder.HasIndex(customer => customer.CustomerReference).IsUnique();
        builder.HasIndex(customer => customer.MaxioCustomerId).IsUnique();
    }
}
