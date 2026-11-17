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

        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.FinixPaymentInstrumentId).HasMaxLength(255).IsRequired();
        builder.Property(p => p.FinixAuthorizationId).HasMaxLength(255);
        builder.Property(p => p.AuthorizationState).HasMaxLength(64);
        builder.Property(p => p.FinixTransferId).HasMaxLength(255);
        builder.Property(p => p.CaptureState).HasMaxLength(64);
        builder.Property(p => p.FailureCode).HasMaxLength(128);
        builder.Property(p => p.FailureMessage).HasMaxLength(1000);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.FinixAuthorizationId);
        builder.HasIndex(p => p.FinixTransferId);

        var navigation = builder.Metadata.FindNavigation(nameof(Payment.Refunds));
        navigation?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
