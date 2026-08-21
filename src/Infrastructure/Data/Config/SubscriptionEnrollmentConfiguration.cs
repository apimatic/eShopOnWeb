using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.Infrastructure.Data.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserKey, x.ProductHandle }).IsUnique();
        builder.HasIndex(x => x.SubscriptionReference).IsUnique();
        builder.Property(x => x.UserKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProductHandle).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SubscriptionReference).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.FailureCode).HasMaxLength(64);
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}
