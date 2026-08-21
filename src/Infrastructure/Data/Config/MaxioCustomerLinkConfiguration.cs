using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.ToTable("MaxioCustomerLinks");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.UserId).HasMaxLength(450).IsRequired();
        builder.Property(link => link.CustomerReference).HasMaxLength(100).IsRequired();
        builder.Property(link => link.LeaseId).HasMaxLength(36);
        builder.Property(link => link.LastError).HasMaxLength(500);
        builder.Property(link => link.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(link => link.Version).IsConcurrencyToken();
        builder.HasIndex(link => link.UserId).IsUnique();
        builder.HasIndex(link => link.CustomerReference).IsUnique();
    }
}

