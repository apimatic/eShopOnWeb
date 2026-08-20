using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");

        builder.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
        builder.HasIndex(x => x.SubscriptionReference).IsUnique();

        builder.Property(x => x.UserId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ProductHandle).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CustomerReference).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SubscriptionReference).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}
