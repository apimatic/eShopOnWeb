using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class ContactNumberConfiguration : IEntityTypeConfiguration<ContactNumber>
{
    public void Configure(EntityTypeBuilder<ContactNumber> builder)
    {
        builder.Property(cn => cn.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(cn => cn.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasIndex(cn => cn.BuyerId);
    }
}
