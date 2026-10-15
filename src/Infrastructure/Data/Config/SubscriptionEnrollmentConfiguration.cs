using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The primary key is the duplicate-prevention claim: every provider (in-memory included) refuses a second insert.
        builder.HasKey(e => e.ClaimKey);

        builder.Property(e => e.ClaimKey)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(e => e.CustomerReference)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(e => e.ProductFamilyHandle)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.UserName)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.PlanHandle)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.SubscriptionReference)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(e => e.SubscriptionReference)
            .IsUnique();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.ConcurrencyStamp)
            .IsConcurrencyToken();
    }
}
