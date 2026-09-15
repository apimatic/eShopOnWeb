using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.ToTable("ContactNumbers");

        builder.Property(c => c.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        // A shopper looks up their own numbers by buyer id; and never registers the same number twice.
        builder.HasIndex(c => new { c.BuyerId, c.PhoneNumber }).IsUnique();
    }
}
