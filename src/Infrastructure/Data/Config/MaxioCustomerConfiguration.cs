using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerConfiguration : IEntityTypeConfiguration<MaxioCustomer>
{
    public void Configure(EntityTypeBuilder<MaxioCustomer> builder)
    {
        builder.Property(c => c.UserId).IsRequired().HasMaxLength(450);
        builder.Property(c => c.Email).IsRequired().HasMaxLength(256);
        builder.Property(c => c.MaxioCustomerId).IsRequired();

        builder.HasIndex(c => c.UserId).IsUnique();
        builder.HasIndex(c => c.MaxioCustomerId).IsUnique();
    }
}
