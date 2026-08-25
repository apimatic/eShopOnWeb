using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.EShopOrderId).IsRequired();
        builder.Property(p => p.PayPalOrderId).IsRequired().HasMaxLength(100);
        builder.Property(p => p.CreateIdempotencyKey).IsRequired().HasMaxLength(200);
        builder.Property(p => p.AuthorizeIdempotencyKey).IsRequired().HasMaxLength(200);

        builder.HasIndex(p => p.EShopOrderId).IsUnique();

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.PaymentId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
