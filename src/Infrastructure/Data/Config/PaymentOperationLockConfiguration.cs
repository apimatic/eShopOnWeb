using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentOperationLockConfiguration : IEntityTypeConfiguration<PaymentOperationLock>
{
    public void Configure(EntityTypeBuilder<PaymentOperationLock> builder)
    {
        builder.HasKey(l => l.OrderId);
        builder.Property(l => l.OrderId).ValueGeneratedNever();
    }
}
