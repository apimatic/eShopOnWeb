using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        // The key is the duplicate-payment claim ("{orderId}-P{attempt}"): never generated, always set by the domain.
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasMaxLength(32).ValueGeneratedNever();
        builder.HasIndex(p => new { p.OrderId, p.AttemptNumber }).IsUnique();

        builder.Property(p => p.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.HasIndex(p => p.IdempotencyKey).IsUnique();
        builder.Property(p => p.MerchantReference).HasMaxLength(80).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.PspReference).HasMaxLength(64);
        builder.Property(p => p.ResultCode).HasMaxLength(40);
        builder.Property(p => p.RefusalReason).HasMaxLength(256);
        builder.Property(p => p.RefusalReasonCode).HasMaxLength(16);
        builder.Property(p => p.ShopperMessage).HasMaxLength(512);

        builder.Ignore(p => p.IsInFlight);
    }
}
