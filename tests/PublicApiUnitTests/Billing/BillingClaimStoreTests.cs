using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingClaimAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.PublicApiUnitTests.Billing;

/// <summary>A CatalogContext on the EF in-memory provider, one scoped context per scope, shared database.</summary>
public sealed class ClaimDatabase : IDisposable
{
    private readonly ServiceProvider _provider;

    public ClaimDatabase()
    {
        var name = "claims-" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(name));
        services.AddScoped<BillingClaimStore>();
        _provider = services.BuildServiceProvider();
    }

    public IServiceScope NewScope() => _provider.CreateScope();

    public BillingClaimStore Store(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<BillingClaimStore>();

    public async Task<TClaim?> ReadAsync<TClaim>(string reference) where TClaim : BillingClaim
    {
        using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogContext>().Set<TClaim>()
            .AsNoTracking().FirstOrDefaultAsync(c => c.Reference == reference);
    }

    public void Dispose() => _provider.Dispose();
}

public class BillingClaimStoreTests
{
    [Fact]
    public async Task SecondClaimForSameReferenceFromAnotherScopedContextIsRefusedByTheStore()
    {
        using var db = new ClaimDatabase();
        using var scopeA = db.NewScope();
        using var scopeB = db.NewScope();
        var now = DateTimeOffset.UtcNow;

        var first = await db.Store(scopeA).TryInsertAsync(new BillingCustomerClaim("eshop-user-abc", now));
        var second = await db.Store(scopeB).TryInsertAsync(new BillingCustomerClaim("eshop-user-abc", now));

        Assert.True(first);
        Assert.False(second);
        var stored = await db.ReadAsync<BillingCustomerClaim>("eshop-user-abc");
        Assert.NotNull(stored);
        Assert.Equal(BillingClaimStatus.Pending, stored!.Status);
    }

    [Fact]
    public async Task InMemoryProviderRaisesArgumentExceptionForADuplicatePrimaryKeyFromAnotherContext()
    {
        // Pins what BillingClaimStore catches on the in-memory provider (SQL Server raises DbUpdateException).
        using var db = new ClaimDatabase();
        using var scopeA = db.NewScope();
        using var scopeB = db.NewScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<CatalogContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<CatalogContext>();
        contextA.BillingCustomerClaims.Add(new BillingCustomerClaim("eshop-user-dup", DateTimeOffset.UtcNow));
        await contextA.SaveChangesAsync();

        contextB.BillingCustomerClaims.Add(new BillingCustomerClaim("eshop-user-dup", DateTimeOffset.UtcNow));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => contextB.SaveChangesAsync());
    }

    [Fact]
    public async Task SecondSubscriptionClaimForSameReferenceIsRefused()
    {
        using var db = new ClaimDatabase();
        using var scopeA = db.NewScope();
        using var scopeB = db.NewScope();
        var now = DateTimeOffset.UtcNow;

        Assert.True(await db.Store(scopeA).TryInsertAsync(new BillingSubscriptionClaim("eshop-sub-1", "eshop-user-1", "eshop-pro", now)));
        Assert.False(await db.Store(scopeB).TryInsertAsync(new BillingSubscriptionClaim("eshop-sub-1", "eshop-user-1", "eshop-pro", now)));
    }

    [Fact]
    public async Task RefusedScopeCanStillReadTheExistingClaim()
    {
        using var db = new ClaimDatabase();
        using var scopeA = db.NewScope();
        using var scopeB = db.NewScope();
        var now = DateTimeOffset.UtcNow;

        await db.Store(scopeA).TryInsertAsync(new BillingCustomerClaim("eshop-user-abc", now));
        Assert.False(await db.Store(scopeB).TryInsertAsync(new BillingCustomerClaim("eshop-user-abc", now)));

        var existing = await db.Store(scopeB).FindAsync<BillingCustomerClaim>("eshop-user-abc");
        Assert.NotNull(existing);
    }

    [Fact]
    public async Task OnlyOneOfTwoConcurrentTakeOversWins()
    {
        using var db = new ClaimDatabase();
        var now = DateTimeOffset.UtcNow;
        using (var seed = db.NewScope())
        {
            var claim = new BillingCustomerClaim("eshop-user-abc", now);
            await db.Store(seed).TryInsertAsync(claim);
            claim.MarkUnknown(now);
            await db.Store(seed).TryUpdateAsync(claim);
        }

        using var scopeA = db.NewScope();
        using var scopeB = db.NewScope();
        var a = await db.Store(scopeA).FindAsync<BillingCustomerClaim>("eshop-user-abc");
        var b = await db.Store(scopeB).FindAsync<BillingCustomerClaim>("eshop-user-abc");
        a!.TakeOver(now.AddSeconds(1));
        b!.TakeOver(now.AddSeconds(1));

        Assert.True(await db.Store(scopeA).TryUpdateAsync(a));
        Assert.False(await db.Store(scopeB).TryUpdateAsync(b));
    }

    [Fact]
    public async Task ReleasedClaimCanBeClaimedAgain()
    {
        using var db = new ClaimDatabase();
        var now = DateTimeOffset.UtcNow;
        using (var scope = db.NewScope())
        {
            var claim = new BillingCustomerClaim("eshop-user-abc", now);
            await db.Store(scope).TryInsertAsync(claim);
            await db.Store(scope).ReleaseAsync(claim);
        }
        using var again = db.NewScope();
        Assert.True(await db.Store(again).TryInsertAsync(new BillingCustomerClaim("eshop-user-abc", now)));
    }
}
