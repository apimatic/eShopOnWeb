using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class MaxioSubscriptionConfiguration : IEntityTypeConfiguration<MaxioSubscription>
{
    public void Configure(EntityTypeBuilder<MaxioSubscription> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.UserId)
            .IsRequired();

        builder.Property(m => m.MaxioCustomerId)
            .IsRequired();

        builder.Property(m => m.MaxioSubscriptionId)
            .IsRequired();

        builder.Property(m => m.ProductHandle)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(m => m.State)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(m => m.CancellationMessage)
            .HasMaxLength(500);

        builder.ToTable("MaxioSubscriptions");
    }
}
