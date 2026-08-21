using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;

public sealed class MaxioCustomerMappingConfiguration : IEntityTypeConfiguration<MaxioCustomerMapping>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerMapping> builder)
    {
        builder.ToTable("MaxioCustomerMappings");
        builder.HasKey(mapping => mapping.UserId);
        builder.Property(mapping => mapping.UserId).HasMaxLength(450);
        builder.Property(mapping => mapping.CustomerReference).HasMaxLength(128).IsRequired();
        builder.HasIndex(mapping => mapping.CustomerReference).IsUnique();
        builder.HasIndex(mapping => mapping.MaxioCustomerId).IsUnique();
        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<MaxioCustomerMapping>(mapping => mapping.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
