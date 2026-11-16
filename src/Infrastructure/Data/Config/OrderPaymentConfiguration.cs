using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.FinixPaymentInstrumentId).HasMaxLength(64);
        builder.Property(p => p.FinixAuthorizationId).HasMaxLength(64);
        builder.Property(p => p.FinixAuthorizationState).HasMaxLength(32);
        builder.Property(p => p.AuthorizationIdempotencyId).HasMaxLength(128);
        builder.Property(p => p.FinixVoidState).HasMaxLength(32);
        builder.Property(p => p.FinixTransferId).HasMaxLength(64);
        builder.Property(p => p.FinixTransferState).HasMaxLength(32);
        builder.Property(p => p.FailureCode).HasMaxLength(64);
        builder.Property(p => p.FailureMessage).HasMaxLength(1000);

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.FinixAuthorizationId);
        builder.HasIndex(p => p.FinixTransferId);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Refunds)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
