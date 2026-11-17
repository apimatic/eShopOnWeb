using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.PaymentInstrumentId).HasMaxLength(64);
        builder.Property(p => p.PaymentReference).HasMaxLength(64).IsRequired();
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationIdempotencyId).HasMaxLength(128);
        builder.Property(p => p.AuthorizationState).HasMaxLength(32);
        builder.Property(p => p.CaptureTransferId).HasMaxLength(64);
        builder.Property(p => p.CaptureState).HasMaxLength(32);
        builder.Property(p => p.VoidState).HasMaxLength(32);

        builder.HasIndex(p => p.OrderId).IsUnique();

        var navigation = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
