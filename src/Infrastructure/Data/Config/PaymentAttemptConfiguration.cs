using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        // The key is the duplicate-charge claim: it is assigned by the application, never generated.
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasMaxLength(64)
            .ValueGeneratedNever();

        builder.HasIndex(a => a.OrderId);

        builder.Property(a => a.IdempotencyKey)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(a => a.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(a => a.PspReference).HasMaxLength(64);
        builder.Property(a => a.ResultCode).HasMaxLength(64);
        builder.Property(a => a.RefusalReason).HasMaxLength(512);
        builder.Property(a => a.RefusalReasonCode).HasMaxLength(32);

        builder.Ignore(a => a.MerchantReference);
        builder.Ignore(a => a.IsTerminal);
    }
}
