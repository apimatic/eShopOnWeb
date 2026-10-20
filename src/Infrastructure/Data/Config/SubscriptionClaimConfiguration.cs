using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionClaimConfiguration : IEntityTypeConfiguration<SubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionClaim> builder)
    {
        builder.ToTable("SubscriptionClaims");

        // The composite primary key is what refuses a second claim for the same (user, plan).
        builder.HasKey(c => new { c.UserId, c.PlanHandle });

        builder.Property(c => c.UserId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.PlanHandle).IsRequired().HasMaxLength(256);
        builder.Property(c => c.State).IsRequired();
        builder.Property(c => c.ClaimedAtUtc).IsRequired();
    }
}

public class MaxioCustomerClaimConfiguration : IEntityTypeConfiguration<MaxioCustomerClaim>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerClaim> builder)
    {
        builder.ToTable("MaxioCustomerClaims");

        builder.HasKey(c => c.UserId);

        builder.Property(c => c.UserId).IsRequired().HasMaxLength(256);
        builder.Property(c => c.ClaimedAtUtc).IsRequired();
    }
}
