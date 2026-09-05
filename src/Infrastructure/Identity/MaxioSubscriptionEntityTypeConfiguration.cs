using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class MaxioSubscriptionEntityTypeConfiguration : IEntityTypeConfiguration<MaxioSubscription>
{
    public void Configure(EntityTypeBuilder<MaxioSubscription> builder)
    {
        builder.ToTable("MaxioSubscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ApplicationUserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.MaxioSubscriptionId).IsRequired();
        builder.Property(x => x.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(x => x.State).IsRequired().HasMaxLength(50);
        builder.Property(x => x.ProductPriceInCents).IsRequired().HasPrecision(18, 2);
        builder.Property(x => x.CurrentPeriodEndsAt).IsRequired();
        builder.Property(x => x.NextAssessmentAt).IsRequired();
        builder.Property(x => x.ActivatedAt).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasIndex(x => x.ApplicationUserId);
    }
}
