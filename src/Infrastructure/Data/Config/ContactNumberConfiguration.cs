using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(x => x.ShopperId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PhoneNumber).IsRequired().HasMaxLength(32);
        builder.HasIndex(x => new { x.ShopperId, x.PhoneNumber }).IsUnique();
    }
}
