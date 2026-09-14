using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
        builder.HasIndex(x => x.CustomerReference).IsUnique();
        builder.HasIndex(x => x.SubscriptionReference).IsUnique();

        builder.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(x => x.CustomerReference).IsRequired().HasMaxLength(128);
        builder.Property(x => x.SubscriptionReference).IsRequired().HasMaxLength(128);
        builder.Property(x => x.LeaseOwner).HasMaxLength(128);
        builder.Property(x => x.ProviderState).HasMaxLength(64);
        builder.Property(x => x.LastSafeError).HasMaxLength(512);
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ReconciliationTarget).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}
