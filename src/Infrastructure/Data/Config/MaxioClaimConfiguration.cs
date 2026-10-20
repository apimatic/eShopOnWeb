using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerClaimConfiguration : IEntityTypeConfiguration<MaxioCustomerClaim>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerClaim> builder)
    {
        // The primary key is the duplicate guard: a second claim for the same user is refused on insert.
        builder.HasKey(c => c.UserName);

        builder.Property(c => c.UserName)
            .IsRequired()
            .HasMaxLength(256);
    }
}

public class MaxioSubscriptionClaimConfiguration : IEntityTypeConfiguration<MaxioSubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionClaim> builder)
    {
        // The primary key is the duplicate guard: a second claim for the same user is refused on insert.
        builder.HasKey(c => c.UserName);

        builder.Property(c => c.UserName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.PlanHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(c => c.Reference)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(c => c.Reference)
            .IsUnique();
    }
}
