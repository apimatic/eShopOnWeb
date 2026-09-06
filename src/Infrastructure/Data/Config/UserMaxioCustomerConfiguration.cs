using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserMaxioCustomerConfiguration : IEntityTypeConfiguration<UserMaxioCustomer>
{
    public void Configure(EntityTypeBuilder<UserMaxioCustomer> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.MaxioCustomerId)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(x => x.UpdatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(x => x.UserId)
            .IsUnique();
    }
}
