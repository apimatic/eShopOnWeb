using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioCustomerResolutionConfiguration : IEntityTypeConfiguration<MaxioCustomerResolution>
{
    public void Configure(EntityTypeBuilder<MaxioCustomerResolution> builder)
    {
        builder.HasKey(x => x.UserName);
        builder.Property(x => x.UserName).HasMaxLength(256);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
    }
}
