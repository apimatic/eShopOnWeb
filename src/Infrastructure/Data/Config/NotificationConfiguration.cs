using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.Property(n => n.BuyerId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(n => n.Kind)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(n => n.ToNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(n => n.Body)
            .HasMaxLength(1600);

        builder.Property(n => n.ProviderMessageSid)
            .HasMaxLength(64);

        builder.Property(n => n.ProviderStatus)
            .HasMaxLength(32);

        builder.Property(n => n.ProviderErrorMessage)
            .HasMaxLength(512);

        builder.Property(n => n.LocalError)
            .HasMaxLength(512);

        builder.Property(n => n.CreatedDate)
            .IsRequired();

        builder.HasIndex(n => n.OrderId);
        builder.HasIndex(n => n.ProviderMessageSid);
    }
}
