using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionEnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.ToTable("SubscriptionEnrollments");
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.ProductHandle).IsRequired().HasMaxLength(255);
        builder.Property(x => x.MaxioCustomerId);
        builder.Property(x => x.MaxioSubscriptionId);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.ProductHandle }).IsUnique();
        builder.HasIndex(x => x.MaxioSubscriptionId).IsUnique();
    }
}
