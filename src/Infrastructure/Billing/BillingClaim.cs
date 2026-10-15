using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

// A primary key insert, persisted BEFORE sending a create, owns the right to send it.
public class BillingClaim
{
    public string Id { get; set; } = "";
    public string Scope { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? ProductHandle { get; set; }
    public string State { get; set; } = "Reserved";
    public int? ProviderId { get; set; }
    public int? CustomerId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class BillingClaimConfiguration : IEntityTypeConfiguration<BillingClaim>
{
    public void Configure(EntityTypeBuilder<BillingClaim> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64).ValueGeneratedNever();
        builder.Property(x => x.Scope).HasMaxLength(64).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.State).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ProductHandle).HasMaxLength(255);
        builder.HasIndex(x => new { x.Scope, x.UserId });
    }
}
