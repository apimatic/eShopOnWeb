using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class MaxioCustomerLinkConfiguration : IEntityTypeConfiguration<MaxioCustomerLink>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerLink> builder)
    {
        builder.ToTable("MaxioCustomerLinks");
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.Reference).IsRequired().HasMaxLength(100);
        builder.Property(x => x.OperationState).IsRequired().HasMaxLength(32);
        builder.Property(x => x.OwnerToken).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ConcurrencyStamp).IsRequired().HasMaxLength(32).IsConcurrencyToken();
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasIndex(x => x.Reference).IsUnique();
        builder.HasIndex(x => x.MaxioCustomerId).IsUnique().HasFilter("[MaxioCustomerId] IS NOT NULL");
    }
}
