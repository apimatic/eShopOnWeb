using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // Primary key (not a unique index): even the in-memory provider refuses a duplicate key.
        builder.HasKey(e => e.UserId);
        builder.Property(e => e.UserId).HasMaxLength(256).ValueGeneratedNever();
        builder.Property(e => e.PlanHandle).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.CustomerReference).IsRequired().HasMaxLength(320);
        builder.Property(e => e.SubscriptionReference).IsRequired().HasMaxLength(320);
        builder.Property(e => e.SubscriptionState).HasMaxLength(64);
    }
}
