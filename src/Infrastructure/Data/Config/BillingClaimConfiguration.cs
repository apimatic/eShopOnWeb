using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

/// <summary>
/// Shared mapping for billing claims. The primary key on <see cref="BillingClaim.Reference"/> is what refuses a
/// second claim for the same write (enforced by SQL Server and by the EF in-memory provider alike); the
/// concurrency token on <see cref="BillingClaim.Version"/> makes lease take-over and completion race-safe.
/// </summary>
internal static class BillingClaimConfiguration
{
    public const int ReferenceMaxLength = 64;

    public static void ConfigureClaim<T>(EntityTypeBuilder<T> builder) where T : BillingClaim
    {
        builder.HasKey(c => c.Reference);
        builder.Property(c => c.Reference).HasMaxLength(ReferenceMaxLength).ValueGeneratedNever();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(c => c.Version).IsConcurrencyToken();
    }
}

public class BillingCustomerLinkConfiguration : IEntityTypeConfiguration<BillingCustomerLink>
{
    public void Configure(EntityTypeBuilder<BillingCustomerLink> builder)
    {
        BillingClaimConfiguration.ConfigureClaim(builder);
        builder.Property(c => c.UserName).IsRequired().HasMaxLength(256);
    }
}

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        BillingClaimConfiguration.ConfigureClaim(builder);
        builder.Property(c => c.CustomerReference).IsRequired().HasMaxLength(BillingClaimConfiguration.ReferenceMaxLength);
        builder.Property(c => c.PlanHandle).IsRequired().HasMaxLength(255);
        builder.HasIndex(c => c.CustomerReference);
    }
}
