using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class CustomerPaymentProfileConfiguration : IEntityTypeConfiguration<CustomerPaymentProfile>
{
    public void Configure(EntityTypeBuilder<CustomerPaymentProfile> builder)
    {
        builder.ToTable("CustomerPaymentProfiles");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.OwnerId).IsRequired().HasMaxLength(256);
        builder.Property(p => p.FinixIdentityId).IsRequired().HasMaxLength(64);

        builder.HasIndex(p => p.OwnerId).IsUnique();
    }
}
