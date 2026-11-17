using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(r => r.State)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(r => r.FinixTransferId).HasMaxLength(64);
        builder.Property(r => r.FinixState).HasMaxLength(32);
        builder.Property(r => r.FailureCode).HasMaxLength(64);
        builder.Property(r => r.FailureMessage).HasMaxLength(1000);

        builder.HasIndex(r => new { r.PaymentId, r.IdempotencyKey }).IsUnique();
    }
}
