using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserMaxioCustomerConfiguration : IEntityTypeConfiguration<UserMaxioCustomer>
{
    public void Configure(EntityTypeBuilder<UserMaxioCustomer> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .UseHiLo("user_maxio_customer_hilo")
            .IsRequired();

        builder.Property(x => x.ApplicationUserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(x => x.MaxioCustomerId)
            .IsRequired();

        builder.HasIndex(x => x.ApplicationUserId)
            .IsUnique();
    }
}
