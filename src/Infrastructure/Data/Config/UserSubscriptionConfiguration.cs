using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class UserSubscriptionConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.UserId).HasMaxLength(255).IsRequired();
        builder.Property(s => s.MaxioSubscriptionId).IsRequired();
        builder.Property(s => s.MaxioCustomerId).IsRequired();
        builder.Property(s => s.SubscriptionPlanId).IsRequired();
        builder.Property(s => s.State).HasMaxLength(100).IsRequired();
        builder.Property(s => s.BalanceInCents).IsRequired();
        builder.Property(s => s.CurrentPeriodEndsAt).IsRequired();
        builder.Property(s => s.NextAssessmentAt).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        builder.HasOne(s => s.SubscriptionPlan)
            .WithMany()
            .HasForeignKey(s => s.SubscriptionPlanId);
    }
}
