using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        // The composite primary key is the duplicate-payment claim: two concurrent pay calls for one order
        // both try to insert the same (OrderId, AttemptNumber), and the store refuses the second.
        builder.HasKey(a => new { a.OrderId, a.AttemptNumber });
        builder.Property(a => a.AttemptNumber).ValueGeneratedNever();

        builder.Property(a => a.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Reference).HasMaxLength(80).IsRequired();
        builder.Property(a => a.Amount).HasColumnType("decimal(18,2)");
        builder.Property(a => a.Currency).HasMaxLength(3).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.PspReference).HasMaxLength(64);
        builder.Property(a => a.ResultCode).HasMaxLength(64);
        builder.Property(a => a.RefusalReason).HasMaxLength(256);
        builder.Property(a => a.RefusalReasonCode).HasMaxLength(64);
        builder.Property(a => a.ErrorCode).HasMaxLength(64);
        builder.Property(a => a.ErrorMessage).HasMaxLength(1024);
    }
}
