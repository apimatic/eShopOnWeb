using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionLinkConfiguration : IEntityTypeConfiguration<MaxioSubscriptionLink>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionLink> builder)
    {
        builder.ToTable("MaxioSubscriptionLinks");
        builder.HasKey(l => l.UserId);
        builder.Property(l => l.UserId).HasMaxLength(450).ValueGeneratedNever();
        builder.Property(l => l.ProductHandle).HasMaxLength(200).IsRequired();
    }
}
