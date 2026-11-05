using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.MaxioSubscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.HasKey(l => l.UserId);
        builder.Property(l => l.UserId).HasMaxLength(256);
        builder.Property(l => l.Reference).HasMaxLength(256).IsRequired();
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(16);
    }
}

public class MaxioSubscriptionClaimConfiguration : IEntityTypeConfiguration<MaxioSubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionClaim> builder)
    {
        builder.HasKey(c => new { c.UserId, c.ProductHandle });
        builder.Property(c => c.UserId).HasMaxLength(256);
        builder.Property(c => c.ProductHandle).HasMaxLength(256);
        builder.Property(c => c.Reference).HasMaxLength(256).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
    }
}
