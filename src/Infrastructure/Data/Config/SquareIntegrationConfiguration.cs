using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data.Config;

public class SquareConnectionConfiguration : IEntityTypeConfiguration<SquareConnection>
{
    public void Configure(EntityTypeBuilder<SquareConnection> builder)
    {
        builder.ToTable("SquareConnections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.MerchantId).IsRequired().HasMaxLength(191);
        builder.Property(c => c.BusinessName).HasMaxLength(255);
        builder.Property(c => c.ProtectedAccessToken).IsRequired();
        builder.Property(c => c.ConcurrencyStamp).IsConcurrencyToken();
    }
}

public class SquareOAuthStateConfiguration : IEntityTypeConfiguration<SquareOAuthState>
{
    public void Configure(EntityTypeBuilder<SquareOAuthState> builder)
    {
        builder.ToTable("SquareOAuthStates");
        builder.HasKey(s => s.StateHash);
        builder.Property(s => s.StateHash).HasMaxLength(64);
        builder.Property(s => s.CreatedBy).IsRequired().HasMaxLength(256);
    }
}

public class SquareCatalogLinkConfiguration : IEntityTypeConfiguration<SquareCatalogLink>
{
    public void Configure(EntityTypeBuilder<SquareCatalogLink> builder)
    {
        builder.ToTable("SquareCatalogLinks");
        builder.HasKey(l => new { l.MerchantId, l.CatalogItemId });
        builder.Property(l => l.MerchantId).HasMaxLength(191);
        builder.Property(l => l.SquareItemId).HasMaxLength(191);
        builder.Property(l => l.SquareVariationId).HasMaxLength(191);
        builder.Property(l => l.State).IsRequired().HasMaxLength(20);
        builder.Property(l => l.PendingIdempotencyKey).HasMaxLength(128);
        builder.Property(l => l.PendingName).HasMaxLength(512);
        builder.Property(l => l.PendingCurrency).HasMaxLength(3);
        builder.Property(l => l.PhotoState).HasMaxLength(20);
        builder.Property(l => l.PhotoSha256).HasMaxLength(64);
        builder.Property(l => l.PhotoIdempotencyKey).HasMaxLength(128);
        builder.Property(l => l.PhotoImageId).HasMaxLength(191);
        builder.Property(l => l.PhotoImageUrl).HasMaxLength(2048);
        builder.Property(l => l.ConcurrencyStamp).IsConcurrencyToken();
    }
}

public class SquareOrderLinkConfiguration : IEntityTypeConfiguration<SquareOrderLink>
{
    public void Configure(EntityTypeBuilder<SquareOrderLink> builder)
    {
        builder.ToTable("SquareOrderLinks");
        builder.HasKey(l => l.OrderId);
        builder.Property(l => l.OrderId).ValueGeneratedNever();
        builder.HasOne(l => l.Order)
            .WithOne()
            .HasForeignKey<SquareOrderLink>(l => l.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property(l => l.MerchantId).IsRequired().HasMaxLength(191);
        builder.Property(l => l.LocationId).IsRequired().HasMaxLength(191);
        builder.Property(l => l.Currency).IsRequired().HasMaxLength(3);
        builder.Property(l => l.IdempotencyKey).IsRequired().HasMaxLength(192);
        builder.Property(l => l.State).IsRequired().HasMaxLength(20);
        builder.Property(l => l.SquareOrderId).HasMaxLength(191);
        builder.Property(l => l.LastErrorCode).HasMaxLength(100);
        builder.Property(l => l.ConcurrencyStamp).IsConcurrencyToken();
    }
}
