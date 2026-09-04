using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class MaxioSubscriptionMappingConfiguration : IEntityTypeConfiguration<MaxioSubscriptionMapping>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionMapping> builder)
    {
        builder.HasKey(mapping => mapping.Id);
        builder.Property(mapping => mapping.UserId).HasMaxLength(450).IsRequired();
        builder.Property(mapping => mapping.CustomerReference).HasMaxLength(255).IsRequired();
        builder.Property(mapping => mapping.ProductHandle).HasMaxLength(255).IsRequired();
        builder.Property(mapping => mapping.SubscriptionReference).HasMaxLength(255).IsRequired();
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(mapping => mapping.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(mapping => new { mapping.UserId, mapping.ProductHandle }).IsUnique();
    }
}
