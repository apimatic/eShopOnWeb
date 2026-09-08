using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.ToTable("MaxioCustomerLinks");

        builder.Property(link => link.UserId)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(link => link.UserId)
            .IsUnique();

        builder.Property(link => link.CustomerReference)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(link => link.CustomerReference)
            .IsUnique();

        builder.Property(link => link.MaxioCustomerId)
            .IsRequired();

        builder.Property(link => link.CreatedAt)
            .IsRequired();
    }
}
