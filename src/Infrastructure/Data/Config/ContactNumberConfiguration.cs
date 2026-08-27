using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(x => x.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CanonicalNumber).HasMaxLength(32).IsRequired();
        builder.Ignore(x => x.IsActive);
        builder.HasIndex(x => new { x.BuyerId, x.CanonicalNumber })
            .IsUnique()
            .HasFilter("[RemovedAt] IS NULL");
    }
}
