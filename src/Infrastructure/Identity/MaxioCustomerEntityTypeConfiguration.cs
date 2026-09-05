using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class MaxioCustomerEntityTypeConfiguration : IEntityTypeConfiguration<MaxioCustomer>
{
    public void Configure(EntityTypeBuilder<MaxioCustomer> builder)
    {
        builder.ToTable("MaxioCustomers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ApplicationUserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.MaxioCustomerId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasIndex(x => x.ApplicationUserId).IsUnique();
    }
}
