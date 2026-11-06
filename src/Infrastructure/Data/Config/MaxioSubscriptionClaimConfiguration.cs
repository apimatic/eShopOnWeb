using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionClaimConfiguration : IEntityTypeConfiguration<MaxioSubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionClaim> builder)
    {
        builder.HasKey(c => new { c.UserId, c.ProductHandle });
        builder.Property(c => c.UserId).HasMaxLength(450);
        builder.Property(c => c.ProductHandle).HasMaxLength(256);
        builder.Property(c => c.Reference).IsRequired().HasMaxLength(800);
    }
}
