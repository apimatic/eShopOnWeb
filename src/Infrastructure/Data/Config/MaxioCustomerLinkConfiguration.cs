using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

/// <summary>
/// The eShopOnWeb user id is the primary key, so the store itself rejects a
/// concurrent second link claim for the same user.
/// </summary>
public sealed class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.ToTable("MaxioCustomerLinks");
        builder.HasKey(link => link.UserId);
        builder.Property(link => link.UserId).HasMaxLength(450).IsRequired();
    }
}