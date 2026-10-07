using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentAttemptConfiguration : IEntityTypeConfiguration<OrderPaymentAttempt>
{
    public void Configure(EntityTypeBuilder<OrderPaymentAttempt> builder)
    {
        // The primary key is the payment claim: a second request claiming the same attempt is refused by the store.
        builder.HasKey(a => new { a.OrderId, a.AttemptNumber });
        builder.Property(a => a.AttemptNumber).ValueGeneratedNever();

        builder.Property(a => a.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(a => a.MerchantReference).HasMaxLength(80).IsRequired();
        builder.Property(a => a.Currency).HasMaxLength(3).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.PspReference).HasMaxLength(64);
        builder.Property(a => a.ResultCode).HasMaxLength(64);
        builder.Property(a => a.RefusalReason).HasMaxLength(512);
        builder.Property(a => a.RefusalReasonCode).HasMaxLength(32);
        builder.Property(a => a.ShopperMessage).HasMaxLength(1024);
    }
}
