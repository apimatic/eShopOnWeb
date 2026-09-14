using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionRecordConfiguration : IEntityTypeConfiguration<SubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<SubscriptionRecord> builder)
    {
        builder.ToTable("SubscriptionRecords");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.MaxioCustomerReference).IsRequired().HasMaxLength(100);
        builder.Property(x => x.SubscriptionReference).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(x => x.State).IsRequired().HasMaxLength(50);

        builder.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
        builder.HasIndex(x => x.MaxioSubscriptionId).IsUnique();
        builder.HasIndex(x => x.SubscriptionReference).IsUnique();
    }
}
