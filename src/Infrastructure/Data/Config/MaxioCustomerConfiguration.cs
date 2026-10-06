using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerConfiguration : IEntityTypeConfiguration<MaxioCustomer>
{
    public void Configure(EntityTypeBuilder<MaxioCustomer> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.UserId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.Reference)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(c => c.UserId).IsUnique();
        builder.HasIndex(c => c.Reference).IsUnique();
    }
}
