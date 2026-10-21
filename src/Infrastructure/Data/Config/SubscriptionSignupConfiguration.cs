using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SubscriptionSignupConfiguration : IEntityTypeConfiguration<SubscriptionSignup>
{
    public void Configure(EntityTypeBuilder<SubscriptionSignup> builder)
    {
        // The primary key is the duplicate claim: a second insert for the same user, plan and attempt
        // is refused by every store, the in-memory one included.
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasMaxLength(512).ValueGeneratedNever();

        builder.Property(s => s.UserId).IsRequired().HasMaxLength(256);
        builder.Property(s => s.CustomerEmail).IsRequired().HasMaxLength(256);
        builder.Property(s => s.PlanHandle).IsRequired().HasMaxLength(200);
        builder.Property(s => s.CustomerReference).IsRequired().HasMaxLength(300);
        builder.Property(s => s.SubscriptionReference).IsRequired().HasMaxLength(512);
        builder.Property(s => s.CustomerUniquenessToken).IsRequired().HasMaxLength(64);
        builder.Property(s => s.SubscriptionUniquenessToken).IsRequired().HasMaxLength(64);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(s => s.ActionLink).HasMaxLength(2048);
        builder.Property(s => s.LastError).HasMaxLength(2048);

        // A lease is taken by an update that only one worker can win.
        builder.Property(s => s.Version).IsConcurrencyToken();

        builder.HasIndex(s => new { s.UserId, s.PlanHandle });
        builder.HasIndex(s => s.Status);
        builder.Ignore(s => s.IsOpen);
    }
}
