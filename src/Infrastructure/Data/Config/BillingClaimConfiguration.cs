using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class BillingClaimConfiguration : IEntityTypeConfiguration<BillingClaim>
{
    public void Configure(EntityTypeBuilder<BillingClaim> builder)
    {
        // The claim key IS the primary key: the store refuses a second claim for the same billing write
        // (SQL Server and the in-memory provider both enforce primary keys).
        builder.HasKey(c => c.ClaimKey);

        builder.Property(c => c.ClaimKey)
            .IsRequired()
            .HasMaxLength(400);

        builder.Property(c => c.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(c => c.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(c => c.UserName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.PlanHandle)
            .HasMaxLength(256);

        builder.Property(c => c.Version)
            .IsConcurrencyToken();

        builder.HasIndex(c => new { c.UserName, c.Kind, c.Status });
    }
}
