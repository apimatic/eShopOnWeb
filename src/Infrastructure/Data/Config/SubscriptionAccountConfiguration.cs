using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionAccountConfiguration : IEntityTypeConfiguration<SubscriptionAccount>
{
    public void Configure(EntityTypeBuilder<SubscriptionAccount> builder)
    {
        builder.ToTable("SubscriptionAccounts");

        builder.Property(account => account.ApplicationUserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.HasIndex(account => account.ApplicationUserId)
            .IsUnique();

        builder.Property(account => account.ApplicationUserEmail)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(account => account.MaxioCustomerId)
            .IsRequired();

        builder.Property(account => account.MaxioCustomerReference)
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(account => account.MaxioCustomerReference)
            .IsUnique();

        builder.Property(account => account.CreatedAtUtc)
            .IsRequired();

        builder.Property(account => account.UpdatedAtUtc)
            .IsRequired(false);
    }
}
