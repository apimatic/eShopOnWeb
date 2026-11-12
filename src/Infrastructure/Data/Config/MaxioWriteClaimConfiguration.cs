using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioWriteClaimConfiguration : IEntityTypeConfiguration<MaxioWriteClaim>
{
    public void Configure(EntityTypeBuilder<MaxioWriteClaim> builder)
    {
        builder.HasKey(c => c.ClaimKey);
        builder.Property(c => c.ClaimKey).HasMaxLength(300).IsRequired();
    }
}
