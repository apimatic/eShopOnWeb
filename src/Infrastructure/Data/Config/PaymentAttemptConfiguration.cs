using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.Property(a => a.Reference).HasMaxLength(80).IsRequired();
        builder.HasIndex(a => a.Reference).IsUnique();
        builder.Property(a => a.IdempotencyKey).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Currency).HasMaxLength(3).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.PspReference).HasMaxLength(64);
        builder.Property(a => a.ResultCode).HasMaxLength(64);
        builder.Property(a => a.RefusalReason).HasMaxLength(512);
        builder.Property(a => a.RefusalReasonCode).HasMaxLength(64);
        builder.Property(a => a.ErrorCode).HasMaxLength(64);
        builder.Property(a => a.ErrorMessage).HasMaxLength(1024);

        builder.HasMany(a => a.ProviderResponses)
            .WithOne()
            .HasForeignKey("PaymentAttemptId")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.ProviderResponses).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(a => a.IsUnsettled);
    }
}
