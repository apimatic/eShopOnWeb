using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.FinixAuthorizationId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationState).HasMaxLength(32);
        builder.Property(p => p.AuthorizationVoidState).HasMaxLength(32);
        builder.Property(p => p.FinixCaptureTransferId).HasMaxLength(64);
        builder.Property(p => p.CaptureState).HasMaxLength(32);
        builder.Property(p => p.FinixPaymentInstrumentId).HasMaxLength(64);
        builder.Property(p => p.CardBrand).HasMaxLength(64);
        builder.Property(p => p.CardLast4).HasMaxLength(4);
        builder.Property(p => p.FailureCode).HasMaxLength(128);
        builder.Property(p => p.FailureMessage).HasMaxLength(1024);

        builder.HasIndex(p => p.OrderId).IsUnique();

        var navigation = builder.Metadata.FindNavigation(nameof(OrderPayment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
