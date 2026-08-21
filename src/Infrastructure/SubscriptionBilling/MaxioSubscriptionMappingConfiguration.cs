using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;

public sealed class MaxioSubscriptionMappingConfiguration : IEntityTypeConfiguration<MaxioSubscriptionMapping>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionMapping> builder)
    {
        builder.ToTable("MaxioSubscriptionMappings");
        builder.HasKey(mapping => mapping.Id);
        builder.Property(mapping => mapping.UserId).HasMaxLength(450).IsRequired();
        builder.Property(mapping => mapping.ProductHandle).HasMaxLength(255).IsRequired();
        builder.Property(mapping => mapping.SubscriptionReference).HasMaxLength(128).IsRequired();
        builder.Property(mapping => mapping.UniquenessToken).HasMaxLength(64).IsRequired();
        builder.Property(mapping => mapping.CreationStatus).HasMaxLength(32).IsRequired();
        builder.Property(mapping => mapping.State).HasMaxLength(32);
        builder.Property(mapping => mapping.Currency).HasMaxLength(8);
        builder.HasIndex(mapping => new { mapping.UserId, mapping.ProductHandle }).IsUnique();
        builder.HasIndex(mapping => mapping.SubscriptionReference).IsUnique();
        builder.HasIndex(mapping => mapping.MaxioSubscriptionId).IsUnique();
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(mapping => mapping.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
