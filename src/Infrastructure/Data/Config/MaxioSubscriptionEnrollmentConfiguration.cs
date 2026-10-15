using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<MaxioSubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<MaxioSubscriptionEnrollment> builder)
    {
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(512);
        builder.Property(x => x.UserName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PlanHandle).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
    }
}
