using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.Infrastructure.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.ToTable("MaxioCustomerLinks");
        builder.HasKey(link => link.Id);
        builder.HasIndex(link => link.UserId).IsUnique();
        builder.HasIndex(link => link.MaxioCustomerId).IsUnique();
        builder.HasIndex(link => link.CustomerReference).IsUnique();
        builder.Property(link => link.UserId).HasMaxLength(450).IsRequired();
        builder.Property(link => link.CustomerReference).HasMaxLength(450).IsRequired();
        builder.Property(link => link.UpdatedAt).IsRequired();
    }
}
