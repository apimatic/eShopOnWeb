using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.FinixAuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationIdempotencyKey).HasMaxLength(255);
        builder.Property(p => p.AuthorizationState).HasMaxLength(32);
        builder.Property(p => p.AuthorizationVoidState).HasMaxLength(32);
        builder.Property(p => p.FinixTransferId).HasMaxLength(64);
        builder.Property(p => p.TransferState).HasMaxLength(32);
        builder.Property(p => p.PaymentInstrumentId).HasMaxLength(64);
        builder.Property(p => p.CardBrand).HasMaxLength(64);
        builder.Property(p => p.Last4).HasMaxLength(4);
        builder.Property(p => p.FailureCode).HasMaxLength(128);
        builder.Property(p => p.FailureMessage).HasMaxLength(1024);
        builder.Property(p => p.FeeAmountCents).HasColumnType("decimal(18,4)");
        builder.Property(p => p.NetAmountCents).HasColumnType("decimal(18,4)");

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.FinixAuthorizationId);
        builder.HasIndex(p => p.FinixTransferId);
    }
}
