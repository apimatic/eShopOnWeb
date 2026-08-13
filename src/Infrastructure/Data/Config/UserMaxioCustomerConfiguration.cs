using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserMaxioCustomerConfiguration : IEntityTypeConfiguration<UserMaxioCustomer>
{
    public void Configure(EntityTypeBuilder<UserMaxioCustomer> builder)
    {
        builder.ToTable("UserMaxioCustomers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ApplicationUserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.MaxioCustomerId)
            .IsRequired();

        builder.Property(x => x.MaxioCustomerReference)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.ApplicationUserId)
            .IsUnique();
    }
}
