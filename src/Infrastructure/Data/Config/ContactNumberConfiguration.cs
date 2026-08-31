using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(c => c.BuyerId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(c => c.PhoneNumber)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(c => c.BuyerId);
    }
}
