using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.ToTable("UserSubscriptions");
        builder.Property(s => s.UserId).HasMaxLength(450).IsRequired();
        builder.Property(s => s.ProductHandle).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Currency).HasMaxLength(10).IsRequired();
        builder.Property(s => s.State).HasMaxLength(50).IsRequired();
        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.MaxioSubscriptionId).IsUnique();
    }
}