using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class MaxioOperationClaimConfiguration : IEntityTypeConfiguration<MaxioOperationClaim>
{
    public void Configure(EntityTypeBuilder<MaxioOperationClaim> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(512);
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
    }
}
