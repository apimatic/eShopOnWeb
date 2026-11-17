using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.PublicReference)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.FulfilmentStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationState).HasMaxLength(32);
        builder.Property(p => p.CaptureTransferId).HasMaxLength(64);
        builder.Property(p => p.CaptureTransferState).HasMaxLength(32);
        builder.Property(p => p.FinixIdentityId).HasMaxLength(64);
        builder.Property(p => p.FinixPaymentInstrumentId).HasMaxLength(64);
        builder.Property(p => p.FailureMessage).HasMaxLength(2000);
        builder.Property(p => p.FeeCents).HasPrecision(18, 6);
        builder.Property(p => p.NetCents).HasPrecision(18, 6);

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.OwnerId);
    }
}

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.Property(r => r.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(r => r.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(r => r.FinixTransferId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.State)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasIndex(r => r.PaymentId);
        builder.HasIndex(r => r.OwnerId);
        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey }).IsUnique();
    }
}

public class SavedCardConfiguration : IEntityTypeConfiguration<SavedCard>
{
    public void Configure(EntityTypeBuilder<SavedCard> builder)
    {
        builder.Property(c => c.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.FinixPaymentInstrumentId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.Brand)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.LastFour)
            .IsRequired()
            .HasMaxLength(4);

        builder.Property(c => c.CardType)
            .HasMaxLength(32);

        builder.HasIndex(c => c.OwnerId);
        builder.HasIndex(c => c.FinixPaymentInstrumentId).IsUnique();
    }
}

public class PaymentCustomerConfiguration : IEntityTypeConfiguration<PaymentCustomer>
{
    public void Configure(EntityTypeBuilder<PaymentCustomer> builder)
    {
        builder.Property(c => c.OwnerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.FinixIdentityId)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(c => c.OwnerId).IsUnique();
    }
}
