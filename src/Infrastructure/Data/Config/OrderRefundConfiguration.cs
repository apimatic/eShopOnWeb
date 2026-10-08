using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        // The key is the duplicate-refund claim: it is assigned by the application, never generated.
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();

        builder.HasIndex(r => r.OrderId);

        builder.Property(r => r.IdempotencyKey)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(r => r.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(r => r.RequestedBy)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(r => r.Reason).HasMaxLength(256);

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(r => r.PspReference).HasMaxLength(64);
        builder.Property(r => r.FailureReason).HasMaxLength(512);

        builder.Ignore(r => r.HoldsReservation);
    }
}
