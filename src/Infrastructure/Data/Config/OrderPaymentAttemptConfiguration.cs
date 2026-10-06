using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentAttemptConfiguration : IEntityTypeConfiguration<OrderPaymentAttempt>
{
    public void Configure(EntityTypeBuilder<OrderPaymentAttempt> builder)
    {
        // The composite key is the claim that prevents two concurrent charges of one order.
        builder.HasKey(a => new { a.OrderId, a.AttemptNumber });
        builder.Property(a => a.AttemptNumber).ValueGeneratedNever();

        builder.HasOne<Order>()
            .WithMany(o => o.PaymentAttempts)
            .HasForeignKey(a => a.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(a => a.MerchantReference).HasMaxLength(80).IsRequired();
        builder.Property(a => a.Currency).HasMaxLength(3).IsRequired();
        builder.Property(a => a.PspReference).HasMaxLength(64);
        builder.Property(a => a.ResultCode).HasMaxLength(64);
        builder.Property(a => a.RefusalReason).HasMaxLength(512);
        builder.Property(a => a.RefusalReasonCode).HasMaxLength(64);
        builder.Property(a => a.ProviderErrorCode).HasMaxLength(64);
        builder.Property(a => a.ProviderMessage).HasMaxLength(1024);
        // Raw provider response, unbounded: support needs every field, including ones added later.
        builder.Property(a => a.ProviderResponse);

        builder.Ignore(a => a.BlocksNewAttempts);
        builder.HasIndex(a => a.PspReference);
    }
}
