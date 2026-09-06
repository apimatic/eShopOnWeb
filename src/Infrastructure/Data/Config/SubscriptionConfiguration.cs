using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.MaxioCustomerId).IsRequired();
        builder.Property(s => s.MaxioSubscriptionId).IsRequired();
        builder.Property(s => s.Reference).IsRequired();
        builder.Property(s => s.ProductHandle).IsRequired();
        builder.Property(s => s.State).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.Reference).IsUnique();
        builder.HasIndex(s => s.MaxioSubscriptionId).IsUnique();
    }
}
