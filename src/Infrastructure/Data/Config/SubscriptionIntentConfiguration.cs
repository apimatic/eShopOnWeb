using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionIntentConfiguration : IEntityTypeConfiguration<SubscriptionIntent>
{
    public void Configure(EntityTypeBuilder<SubscriptionIntent> builder)
    {
        builder.ToTable("SubscriptionIntents");
        builder.HasKey(intent => intent.Id);
        builder.Property(intent => intent.UserId).HasMaxLength(256).IsRequired();
        builder.Property(intent => intent.ProductHandle).HasMaxLength(255).IsRequired();
        builder.Property(intent => intent.SubscriptionReference).HasMaxLength(64).IsRequired();
        builder.Property(intent => intent.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(intent => new { intent.UserId, intent.ProductHandle }).IsUnique();
        builder.HasIndex(intent => intent.SubscriptionReference).IsUnique();
    }
}
