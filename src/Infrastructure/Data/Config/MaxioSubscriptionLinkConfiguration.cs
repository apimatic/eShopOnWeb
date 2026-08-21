using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class MaxioSubscriptionLinkConfiguration : IEntityTypeConfiguration<MaxioSubscriptionLink>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionLink> builder)
    {
        builder.ToTable("MaxioSubscriptionLinks");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.UserId).HasMaxLength(450).IsRequired();
        builder.Property(link => link.ProductHandle).HasMaxLength(100).IsRequired();
        builder.Property(link => link.SubscriptionReference).HasMaxLength(100).IsRequired();
        builder.Property(link => link.ProductName).HasMaxLength(200);
        builder.Property(link => link.ProviderState).HasMaxLength(50);
        builder.Property(link => link.LeaseId).HasMaxLength(36);
        builder.Property(link => link.LastError).HasMaxLength(500);
        builder.Property(link => link.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(link => link.Version).IsConcurrencyToken();
        builder.HasIndex(link => new { link.UserId, link.ProductHandle }).IsUnique();
        builder.HasIndex(link => link.SubscriptionReference).IsUnique();
    }
}

