using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBillingAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public sealed class SubscriptionReservationConfiguration : IEntityTypeConfiguration<SubscriptionReservation>
{
    public void Configure(EntityTypeBuilder<SubscriptionReservation> builder)
    {
        builder.Property(reservation => reservation.UserId).HasMaxLength(450).IsRequired();
        builder.Property(reservation => reservation.ProductHandle).HasMaxLength(255).IsRequired();
        builder.Property(reservation => reservation.CustomerReference).HasMaxLength(255).IsRequired();
        builder.Property(reservation => reservation.SubscriptionReference).HasMaxLength(255).IsRequired();
        builder.Property(reservation => reservation.Status).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(reservation => new { reservation.UserId, reservation.ProductHandle }).IsUnique();
        builder.HasIndex(reservation => reservation.CustomerReference);
        builder.HasIndex(reservation => reservation.SubscriptionReference).IsUnique();
    }
}
