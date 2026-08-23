using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class RecurringSubscriptionConfiguration : IEntityTypeConfiguration<RecurringSubscription>
{
    public void Configure(EntityTypeBuilder<RecurringSubscription> builder)
    {
        builder.ToTable("RecurringSubscriptions");
        builder.Property(x => x.ApplicationUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.ProductHandle).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(8);
        builder.Property(x => x.ProviderState).HasMaxLength(40);
        builder.Property(x => x.MaxioSubscriptionReference).HasMaxLength(100).IsRequired();
        builder.Property(x => x.OperationStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.ApplicationUserId, x.ProductHandle }).IsUnique();
        builder.HasIndex(x => x.MaxioSubscriptionReference).IsUnique();
        builder.HasIndex(x => x.MaxioSubscriptionId).IsUnique().HasFilter("[MaxioSubscriptionId] IS NOT NULL");
    }
}
