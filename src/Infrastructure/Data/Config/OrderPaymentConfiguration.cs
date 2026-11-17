using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.Status)
            .IsRequired();

        builder.Property(p => p.FinixAuthorizationId).HasMaxLength(64);
        builder.Property(p => p.FinixAuthorizationState).HasMaxLength(32);
        builder.Property(p => p.FinixCaptureTransferId).HasMaxLength(64);
        builder.Property(p => p.FinixCaptureState).HasMaxLength(32);
        builder.Property(p => p.FailureCode).HasMaxLength(64);
        builder.Property(p => p.FailureMessage).HasMaxLength(512);

        builder.HasIndex(p => p.OrderId).IsUnique();

        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Refunds)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
