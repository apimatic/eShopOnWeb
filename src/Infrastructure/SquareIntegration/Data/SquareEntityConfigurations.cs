using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration.Data;

public class SquareMerchantConnectionConfiguration : IEntityTypeConfiguration<SquareMerchantConnection>
{
    public void Configure(EntityTypeBuilder<SquareMerchantConnection> builder)
    {
        builder.ToTable("SquareMerchantConnections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.MerchantId).IsRequired().HasMaxLength(64);
        builder.Property(c => c.ProtectedAccessToken).IsRequired().HasMaxLength(4000);
        builder.Property(c => c.ProtectedRefreshToken).HasMaxLength(4000);
    }
}

public class SquareOAuthStateConfiguration : IEntityTypeConfiguration<SquareOAuthState>
{
    public void Configure(EntityTypeBuilder<SquareOAuthState> builder)
    {
        builder.ToTable("SquareOAuthStates");
        builder.HasKey(s => s.State);
        builder.Property(s => s.State).HasMaxLength(128);
        builder.Property(s => s.StartedBy).HasMaxLength(256);
    }
}

public class SquareCatalogLinkConfiguration : IEntityTypeConfiguration<SquareCatalogLink>
{
    public void Configure(EntityTypeBuilder<SquareCatalogLink> builder)
    {
        builder.ToTable("SquareCatalogLinks");
        builder.HasKey(l => new { l.MerchantId, l.CatalogItemId });
        builder.Property(l => l.MerchantId).HasMaxLength(64);
        builder.Property(l => l.SquareItemId).IsRequired().HasMaxLength(192);
        builder.Property(l => l.SquareVariationId).IsRequired().HasMaxLength(192);
        builder.Property(l => l.SquareImageId).HasMaxLength(192);
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
        builder.Property(l => l.MerchantId).IsRequired().HasMaxLength(64);
        builder.Property(l => l.LocationId).IsRequired().HasMaxLength(64);
        builder.Property(l => l.IdempotencyKey).IsRequired().HasMaxLength(64);
        builder.Property(l => l.SquareOrderId).HasMaxLength(192);
        builder.Property(l => l.LineCatalogObjectIds).IsRequired().HasMaxLength(4000);
    }
}

public class SquareLeaseConfiguration : IEntityTypeConfiguration<SquareLease>
{
    public void Configure(EntityTypeBuilder<SquareLease> builder)
    {
        builder.ToTable("SquareLeases");
        builder.HasKey(l => l.Name);
        builder.Property(l => l.Name).HasMaxLength(200);
        builder.Property(l => l.Owner).IsRequired().HasMaxLength(64);
    }
}
