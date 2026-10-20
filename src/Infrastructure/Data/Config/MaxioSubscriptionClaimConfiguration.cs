using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionClaimConfiguration : IEntityTypeConfiguration<MaxioSubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionClaim> builder)
    {
        // The composite primary key is the duplicate guard: one subscription per user per plan.
        builder.HasKey(c => new { c.UserName, c.ProductHandle });

        builder.Property(c => c.UserName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.ProductHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.Reference)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.Status)
            .IsRequired();

        builder.Property(c => c.LeaseId)
            .IsConcurrencyToken();
    }
}
