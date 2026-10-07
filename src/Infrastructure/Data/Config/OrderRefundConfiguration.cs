using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.Property(r => r.Reference).HasMaxLength(80).IsRequired();
        builder.HasIndex(r => r.Reference).IsUnique();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(r => r.PaymentPspReference).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.RequestedBy).HasMaxLength(256).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.PspReference).HasMaxLength(64);
        builder.Property(r => r.ErrorCode).HasMaxLength(64);
        builder.Property(r => r.ErrorMessage).HasMaxLength(1024);

        builder.HasMany(r => r.ProviderResponses)
            .WithOne()
            .HasForeignKey("OrderRefundId")
            .OnDelete(DeleteBehavior.NoAction);
        builder.Navigation(r => r.ProviderResponses).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(r => r.IsUnsettled);
        builder.Ignore(r => r.CountsAgainstPayment);
    }
}
