using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionBillingLinkConfiguration : IEntityTypeConfiguration<SubscriptionBillingLink>
{
    public void Configure(EntityTypeBuilder<SubscriptionBillingLink> builder)
    {
        builder.ToTable("SubscriptionBillingLinks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ProductHandle).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CustomerReference).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SubscriptionReference).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LeaseToken).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ConcurrencyToken).HasMaxLength(32).IsConcurrencyToken().IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
        builder.HasIndex(x => x.SubscriptionReference).IsUnique();
    }
}
