using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(number => number.BuyerId).HasMaxLength(256).IsRequired();
        builder.Property(number => number.CanonicalNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(number => new { number.BuyerId, number.CanonicalNumber })
            .IsUnique()
            .HasFilter("[RemovedAt] IS NULL");
        builder.Ignore(number => number.IsActive);
    }
}
