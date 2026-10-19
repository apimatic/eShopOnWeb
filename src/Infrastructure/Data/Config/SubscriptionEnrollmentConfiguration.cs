using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.Infrastructure.Billing;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");

        // The primary key is the duplicate guard: every provider (SQL Server and the in-memory
        // test provider alike) refuses a second row for the same user.
        builder.HasKey(e => e.UserName);

        builder.Property(e => e.UserName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.PlanHandle)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.SubscriptionReference)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(e => e.Status)
            .IsRequired();
    }
}
