using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

public class EfPaymentOperationLockStoreTests : IDisposable
{
    private readonly PaymentTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private IPaymentOperationLock NewLock(out IServiceScope scope)
    {
        scope = _host.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPaymentOperationLock>();
    }

    [Fact]
    public async Task SecondClaim_IsRefusedByTheStore_UntilReleased()
    {
        var first = NewLock(out var s1);
        var second = NewLock(out var s2);
        using (s1)
        using (s2)
        {
            Assert.True(await first.TryAcquireAsync(42, CancellationToken.None));
            Assert.False(await second.TryAcquireAsync(42, CancellationToken.None));
            Assert.True(await second.TryAcquireAsync(43, CancellationToken.None)); // other orders are unaffected

            await first.ReleaseAsync(42);

            Assert.True(await second.TryAcquireAsync(42, CancellationToken.None));
        }
    }

    [Fact]
    public async Task RacingClaims_AreRefusedByThePrimaryKey()
    {
        // Two callers that both passed the read: the store itself must refuse the second insert.
        using var s1 = _host.CreateScope();
        using var s2 = _host.CreateScope();
        var db1 = s1.ServiceProvider.GetRequiredService<CatalogContext>();
        var db2 = s2.ServiceProvider.GetRequiredService<CatalogContext>();
        db1.PaymentOperationLocks.Add(new PaymentOperationLock(99, Guid.NewGuid(), DateTimeOffset.UtcNow));
        db2.PaymentOperationLocks.Add(new PaymentOperationLock(99, Guid.NewGuid(), DateTimeOffset.UtcNow));

        await db1.SaveChangesAsync();

        // SQL Server raises DbUpdateException; the in-memory store raises ArgumentException. The claim store catches both.
        var refusal = await Assert.ThrowsAnyAsync<Exception>(() => db2.SaveChangesAsync());
        Assert.True(refusal is DbUpdateException or ArgumentException, refusal.GetType().FullName);
    }

    [Fact]
    public async Task ClaimInsert_IsWhatRefusesTheSecondCaller()
    {
        // A claim row already committed by another instance; this store has never read it.
        using (var s = _host.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<CatalogContext>();
            db.PaymentOperationLocks.Add(new PaymentOperationLock(5, Guid.NewGuid(), _host.Clock.GetUtcNow()));
            await db.SaveChangesAsync();
        }

        var store = NewLock(out var scope);
        using (scope)
        {
            Assert.False(await store.TryAcquireAsync(5, CancellationToken.None));
        }
    }

    [Fact]
    public async Task AbandonedClaim_ExpiresSoACrashedHolderCannotBlockForever()
    {
        var crashed = NewLock(out var s1);
        var next = NewLock(out var s2);
        using (s1)
        using (s2)
        {
            Assert.True(await crashed.TryAcquireAsync(7, CancellationToken.None));
            Assert.False(await next.TryAcquireAsync(7, CancellationToken.None));

            _host.Clock.Now += EfPaymentOperationLockStore.Expiry + TimeSpan.FromSeconds(1);

            Assert.True(await next.TryAcquireAsync(7, CancellationToken.None));
            // The crashed holder's late release must not remove the new holder's claim.
            await crashed.ReleaseAsync(7);
            Assert.False(await NewLock(out var s3).TryAcquireAsync(7, CancellationToken.None));
            s3.Dispose();
        }
    }
}
