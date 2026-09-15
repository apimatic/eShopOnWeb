using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class PaymentRecordConfiguration : IEntityTypeConfiguration<PaymentRecord>
{
    public void Configure(EntityTypeBuilder<PaymentRecord> builder)
    {
        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.OrderAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.AuthorizedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CapturedAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PayPalFee).HasColumnType("decimal(18,2)");
        builder.Property(x => x.MerchantNet).HasColumnType("decimal(18,2)");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasMany(x => x.Refunds)
            .WithOne()
            .HasForeignKey(x => x.PaymentRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
