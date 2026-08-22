using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.CapturedAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.PaypalFee)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.NetProceeds)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.CustomId).HasMaxLength(64);
        builder.Property(p => p.InvoiceId).HasMaxLength(127);
        builder.Property(p => p.PayIdempotencyKey).HasMaxLength(100);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationStatus).HasMaxLength(32);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.CaptureStatus).HasMaxLength(32);

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.BuyerId);

        builder.OwnsMany(p => p.Refunds, r =>
        {
            r.ToTable("PaymentRefunds");
            r.WithOwner().HasForeignKey("OrderPaymentId");
            r.HasKey(x => x.Id);
            r.Property(x => x.Id).ValueGeneratedOnAdd();
            r.Property(x => x.PayPalRefundId).HasMaxLength(64).IsRequired();
            r.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            r.Property(x => x.Status).HasMaxLength(32).IsRequired();
            r.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
            r.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            r.HasIndex(x => x.IdempotencyKey);
        });

        builder.Navigation(p => p.Refunds)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
