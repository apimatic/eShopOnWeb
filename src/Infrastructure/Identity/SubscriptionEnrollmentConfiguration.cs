using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

internal sealed class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ProductHandle).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CustomerReference).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SubscriptionReference).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.OwnerToken).HasMaxLength(64);
        builder.Property(x => x.PlanName).HasMaxLength(255);
        builder.Property(x => x.BillingInterval).HasMaxLength(64);
        builder.Property(x => x.ProviderState).HasMaxLength(64);
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}
