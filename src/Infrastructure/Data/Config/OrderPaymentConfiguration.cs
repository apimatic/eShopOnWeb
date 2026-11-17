using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.FinixIdentityId).HasMaxLength(64);
        builder.Property(p => p.PaymentInstrumentId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationState).HasMaxLength(32);
        builder.Property(p => p.CaptureTransferId).HasMaxLength(64);
        builder.Property(p => p.CaptureState).HasMaxLength(32);
        builder.Property(p => p.FailureCode).HasMaxLength(100);
        builder.Property(p => p.FailureMessage).HasMaxLength(1000);

        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds))
            ?.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
