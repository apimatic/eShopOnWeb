using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionProvisioningIntentConfiguration
    : IEntityTypeConfiguration<SubscriptionProvisioningIntent>
{
    public void Configure(EntityTypeBuilder<SubscriptionProvisioningIntent> builder)
    {
        builder.ToTable("SubscriptionProvisioningIntents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserReference).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ProductHandle).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SubscriptionReference).HasMaxLength(80).IsRequired();
        builder.Property(x => x.LeaseToken).HasMaxLength(36).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.UserReference, x.ProductHandle }).IsUnique();
        builder.HasIndex(x => x.SubscriptionReference).IsUnique();
    }
}
