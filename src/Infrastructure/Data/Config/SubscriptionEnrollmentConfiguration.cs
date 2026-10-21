using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        // The primary key is the duplicate guard: one enrollment in flight (or live) per user.
        builder.HasKey(e => e.UserId);

        builder.Property(e => e.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(e => e.SubscriptionReference)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.PlanHandle)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.ConcurrencyStamp)
            .IsConcurrencyToken();

        builder.Ignore(e => e.IsCompleted);
    }
}
