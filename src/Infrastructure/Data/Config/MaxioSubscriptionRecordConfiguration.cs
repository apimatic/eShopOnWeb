using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionRecordConfiguration : IEntityTypeConfiguration<MaxioSubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionRecord> builder)
    {
        builder.Property(r => r.UserId).HasMaxLength(450).IsRequired();
        builder.Property(r => r.UserName).HasMaxLength(256).IsRequired();
        builder.Property(r => r.ProductHandle).HasMaxLength(100).IsRequired();
        builder.Property(r => r.MaxioSubscriptionReference).HasMaxLength(300);
        builder.HasIndex(r => new { r.UserId, r.ProductHandle }).IsUnique();
        builder.HasIndex(r => r.MaxioSubscriptionId).IsUnique();
    }
}
