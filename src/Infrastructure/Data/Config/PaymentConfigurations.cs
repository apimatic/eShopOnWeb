using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.FinixOperationId).HasMaxLength(64).IsRequired();
        builder.Property(p => p.FeeCents).HasColumnType("decimal(18,4)");
        builder.Property(p => p.FinixAuthorizationId).HasMaxLength(128);
        builder.Property(p => p.AuthorizationState).HasMaxLength(32);
        builder.Property(p => p.CaptureTransferId).HasMaxLength(128);
        builder.Property(p => p.CaptureState).HasMaxLength(32);
        builder.Property(p => p.FinixPaymentInstrumentId).HasMaxLength(128);
        builder.Property(p => p.FinixIdentityId).HasMaxLength(128);
        builder.Property(p => p.FailureCode).HasMaxLength(64);
        builder.Property(p => p.FailureMessage).HasMaxLength(1024);

        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("PaymentRefunds");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.FinixRefundId).HasMaxLength(128).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(r => r.State).HasMaxLength(32);
        builder.Property(r => r.FailureCode).HasMaxLength(64);
        builder.Property(r => r.FailureMessage).HasMaxLength(1024);
        builder.HasIndex(r => new { r.OrderPaymentId, r.IdempotencyKey }).IsUnique();
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(p => p.FinixPaymentInstrumentId).HasMaxLength(128).IsRequired();
        builder.Property(p => p.FinixIdentityId).HasMaxLength(128);
        builder.Property(p => p.Brand).HasMaxLength(64);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.Property(p => p.CardType).HasMaxLength(64);
        builder.HasIndex(p => p.FinixPaymentInstrumentId).IsUnique();
        builder.HasIndex(p => p.BuyerId);
    }
}
