using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");
        builder.Property(e => e.UserId).IsRequired().HasMaxLength(450);
        builder.Property(e => e.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(e => e.SubscriptionReference).IsRequired().HasMaxLength(255);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.RowVersion).IsRowVersion();
        builder.HasIndex(e => new { e.UserId, e.ProductHandle }).IsUnique();
        builder.HasIndex(e => e.SubscriptionReference).IsUnique();
    }
}
