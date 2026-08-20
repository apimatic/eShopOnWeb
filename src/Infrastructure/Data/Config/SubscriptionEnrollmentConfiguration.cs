using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.HasKey(enrollment => enrollment.Id);
        builder.Property(enrollment => enrollment.Id).ValueGeneratedOnAdd();
        builder.Property(enrollment => enrollment.UserId).IsRequired().HasMaxLength(450);
        builder.Property(enrollment => enrollment.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(enrollment => enrollment.SubscriptionReference).IsRequired().HasMaxLength(750);
        builder.Property(enrollment => enrollment.CreatedAtUtc).IsRequired();
        builder.Property(enrollment => enrollment.UpdatedAtUtc).IsRequired();
        builder.HasIndex(enrollment => new { enrollment.UserId, enrollment.ProductHandle }).IsUnique();
        builder.HasIndex(enrollment => enrollment.SubscriptionReference).IsUnique();
        builder.HasIndex(enrollment => enrollment.MaxioSubscriptionId).IsUnique()
            .HasFilter("[MaxioSubscriptionId] IS NOT NULL");
    }
}
