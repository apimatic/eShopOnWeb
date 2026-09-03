using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;
namespace Microsoft.eShopWeb.Infrastructure.Data.Config;
public class PaymentConfiguration : IEntityTypeConfiguration<PaymentMethod>, IEntityTypeConfiguration<OrderPayment>, IEntityTypeConfiguration<OrderRefund>
{
 public void Configure(EntityTypeBuilder<PaymentMethod> b){ b.HasIndex(x=>new{x.BuyerId,x.PayPalTokenId}).IsUnique(); b.Property(x=>x.BuyerId).HasMaxLength(256).IsRequired(); b.Property(x=>x.PayPalTokenId).HasMaxLength(255).IsRequired(); }
 public void Configure(EntityTypeBuilder<OrderPayment> b){ b.HasIndex(x=>x.OrderId).IsUnique(); b.Property(x=>x.BuyerId).HasMaxLength(256).IsRequired(); b.Property(x=>x.Amount).HasPrecision(18,2); b.Property(x=>x.CapturedAmount).HasPrecision(18,2); b.Property(x=>x.PayPalFee).HasPrecision(18,2); b.Property(x=>x.NetAmount).HasPrecision(18,2); }
 public void Configure(EntityTypeBuilder<OrderRefund> b){ b.HasIndex(x=>new{x.OrderId,x.IdempotencyKey}).IsUnique(); b.Property(x=>x.Amount).HasPrecision(18,2); }
}
