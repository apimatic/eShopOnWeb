using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerClaimConfiguration : IEntityTypeConfiguration<MaxioCustomerClaim>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerClaim> builder)
    {
        builder.HasKey(c => c.Reference);

        builder.Property(c => c.Reference)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.Version)
            .IsConcurrencyToken();
    }
}

public class MaxioSubscriptionClaimConfiguration : IEntityTypeConfiguration<MaxioSubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionClaim> builder)
    {
        builder.HasKey(c => c.UserName);

        builder.Property(c => c.UserName)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(c => c.SubscriptionReference)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(c => c.ProductHandle)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.Version)
            .IsConcurrencyToken();
    }
}
