using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerConfiguration : IEntityTypeConfiguration<MaxioCustomer>
{
    public void Configure(EntityTypeBuilder<MaxioCustomer> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.MaxioReference).IsRequired().HasMaxLength(250);
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasIndex(x => x.MaxioReference).IsUnique();
    }
}
