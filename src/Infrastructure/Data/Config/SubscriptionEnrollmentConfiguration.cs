using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");
        builder.HasIndex(item => new { item.UserId, item.ProductHandle }).IsUnique();
        builder.HasIndex(item => item.Reference).IsUnique();
        builder.Property(item => item.UserId).IsRequired().HasMaxLength(450);
        builder.Property(item => item.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(item => item.Reference).IsRequired().HasMaxLength(255);
        builder.Property(item => item.AttemptToken).IsRequired().HasMaxLength(32);
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
    }
}
