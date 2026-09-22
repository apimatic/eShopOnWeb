using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        // OrderId is the primary key (1:1 with Order) — primary-key uniqueness is enforced by both the
        // SqlServer and in-memory providers, so a second concurrent authorize insert is rejected.
        builder.HasKey(p => p.OrderId);
        builder.Property(p => p.OrderId).ValueGeneratedNever();

        builder.Property(p => p.BuyerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.InvoiceId).IsRequired().HasMaxLength(127);
        builder.Property(p => p.Status).HasConversion<int>();

        builder.Property(p => p.AuthorizedAmount).HasColumnType("decimal(18,4)");
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,4)");
        builder.Property(p => p.PayPalFee).HasColumnType("decimal(18,4)");
        builder.Property(p => p.NetAmount).HasColumnType("decimal(18,4)");
        builder.Property(p => p.RefundedAmount).HasColumnType("decimal(18,4)");

        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationExpiresAt).HasMaxLength(64);
        builder.Property(p => p.LastError).HasMaxLength(1024);
    }
}
