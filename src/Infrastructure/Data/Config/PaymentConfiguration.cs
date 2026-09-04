using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;
public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b) { b.Property(x => x.BuyerId).IsRequired().HasMaxLength(256); b.Property(x => x.Currency).HasMaxLength(3).IsRequired(); b.Property(x => x.Status).HasMaxLength(40).IsRequired(); b.Property(x => x.Amount).HasPrecision(18, 2); b.Property(x => x.CapturedAmount).HasPrecision(18, 2); b.Property(x => x.PayPalFee).HasPrecision(18, 2); b.Property(x => x.NetAmount).HasPrecision(18, 2); b.HasIndex(x => x.OrderId).IsUnique(); }
}
public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> b) { b.Property(x => x.BuyerId).IsRequired().HasMaxLength(256); b.Property(x => x.VaultId).IsRequired().HasMaxLength(255); b.HasIndex(x => new { x.BuyerId, x.VaultId }).IsUnique(); }
}
public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> b) { b.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(200); b.Property(x => x.Amount).HasPrecision(18, 2); b.HasIndex(x => new { x.PaymentId, x.IdempotencyKey }).IsUnique(); }
}
