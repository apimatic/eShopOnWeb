using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionClaimConfiguration : IEntityTypeConfiguration<SubscriptionClaim>
{
    public void Configure(EntityTypeBuilder<SubscriptionClaim> builder)
    {
        builder.ToTable("SubscriptionClaims");

        // The primary key ("{userId}|{planHandle}") is the claim: a double-submit is refused by the store.
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .IsRequired()
            .HasMaxLength(700);

        builder.Property(c => c.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(c => c.PlanHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(c => c.Reference)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.ClaimToken)
            .IsConcurrencyToken();

        builder.HasIndex(c => c.UserId);
    }
}
