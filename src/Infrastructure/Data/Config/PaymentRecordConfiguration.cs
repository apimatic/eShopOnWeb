using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentRecordConfiguration : IEntityTypeConfiguration<PaymentRecord>
{
    public void Configure(EntityTypeBuilder<PaymentRecord> builder)
    {
        builder.HasIndex(p => p.OrderId).IsUnique();
        builder.HasIndex(p => p.ExternalReference).IsUnique();
        builder.Property(p => p.ExternalReference).IsRequired().HasMaxLength(64);
        builder.Property(p => p.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.OrderAmount).HasPrecision(18, 2);
        builder.Property(p => p.AuthorizedAmount).HasPrecision(18, 2);
        builder.Property(p => p.CapturedAmount).HasPrecision(18, 2);
        builder.Property(p => p.PayPalFee).HasPrecision(18, 2);
        builder.Property(p => p.NetAmount).HasPrecision(18, 2);
        builder.Property(p => p.PayPalOrderId).HasMaxLength(64);
        builder.Property(p => p.AuthorizationId).HasMaxLength(64);
        builder.Property(p => p.CaptureId).HasMaxLength(64);
        builder.Property(p => p.RowVersion).IsRowVersion();

        var refunds = builder.Metadata.FindNavigation(nameof(PaymentRecord.Refunds));
        refunds?.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.PaymentRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
