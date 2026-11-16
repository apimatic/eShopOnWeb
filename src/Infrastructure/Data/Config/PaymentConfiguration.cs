using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.Provider)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(p => p.AuthorizationIdempotencySeed)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationState).HasMaxLength(32);
        builder.Property(p => p.CaptureTransferId).HasMaxLength(64);
        builder.Property(p => p.CaptureState).HasMaxLength(32);
        builder.Property(p => p.CardBrand).HasMaxLength(64);
        builder.Property(p => p.CardLast4).HasMaxLength(4);
        builder.Property(p => p.FailureCode).HasMaxLength(64);
        builder.Property(p => p.FailureMessage).HasMaxLength(512);

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.AuthorizationId);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        var navigation = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds");

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(r => r.ProviderTransferId).HasMaxLength(64);
        builder.Property(r => r.ProviderState).HasMaxLength(32);

        builder.HasIndex(r => new { r.PaymentId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.ProviderTransferId);
    }
}

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("SavedPaymentMethods");

        builder.Property(m => m.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(m => m.FinixIdentityId).HasMaxLength(64);
        builder.Property(m => m.Brand).HasMaxLength(64);
        builder.Property(m => m.Last4).HasMaxLength(4);
        builder.Property(m => m.CardholderName).HasMaxLength(200);

        builder.HasIndex(m => new { m.BuyerId, m.FinixPaymentInstrumentId }).IsUnique();
        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}
