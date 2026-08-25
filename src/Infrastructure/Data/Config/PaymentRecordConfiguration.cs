using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRecordConfiguration : IEntityTypeConfiguration<PaymentRecord>
{
    public void Configure(EntityTypeBuilder<PaymentRecord> builder)
    {
        builder.Property(p => p.Currency).HasMaxLength(10).IsRequired();
        builder.Property(p => p.PayPalOrderId).HasMaxLength(50);
        builder.Property(p => p.AuthorizationId).HasMaxLength(50);
        builder.Property(p => p.CaptureId).HasMaxLength(50);
        builder.Property(p => p.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedFee).HasColumnType("decimal(18,2)");
        builder.Property(p => p.CapturedNet).HasColumnType("decimal(18,2)");
        builder.Property(p => p.TotalRefundedAmount).HasColumnType("decimal(18,2)");

        var nav = builder.Metadata.FindNavigation(nameof(PaymentRecord.Refunds));
        nav?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Refunds)
               .WithOne()
               .HasForeignKey(r => r.PaymentRecordId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
