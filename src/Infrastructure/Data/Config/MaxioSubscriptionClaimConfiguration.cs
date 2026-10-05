using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

/// <summary>
/// The deterministic Maxio subscription reference is the primary key, so the store
/// itself rejects a concurrent second claim for the same (user, plan) pair.
/// </summary>
public sealed class MaxioSubscriptionClaimConfiguration : IEntityTypeConfiguration<MaxioSubscriptionClaim>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<MaxioSubscriptionClaim> builder)
    {
        builder.ToTable("MaxioSubscriptionClaims");
        builder.HasKey(claim => claim.RequestId);
        builder.Property(claim => claim.RequestId).HasMaxLength(450).IsRequired();
        builder.Property(claim => claim.UserId).HasMaxLength(450).IsRequired();
        builder.Property(claim => claim.ProductHandle).HasMaxLength(256).IsRequired();
        builder.Property(claim => claim.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(claim => claim.UserId);
    }
}