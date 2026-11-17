using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Durable, store-backed claims for money-moving operations. The claim row is
/// committed before the Finix call; a second caller for the same key is stopped
/// before it reaches the provider. A per-key in-process gate serializes the
/// check-and-insert so a double-click in this host cannot race the claim.
/// </summary>
public sealed class PaymentClaimStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.Ordinal);

    private readonly CatalogContext _db;

    public PaymentClaimStore(CatalogContext db)
    {
        _db = db;
    }

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    public async Task<PaymentOperationClaim?> FindAsync(string key, CancellationToken cancellationToken) =>
        await _db.PaymentOperationClaims.FindAsync(new object[] { key }, cancellationToken);

    public async Task<bool> TryAddAsync(PaymentOperationClaim claim, CancellationToken cancellationToken)
    {
        _db.PaymentOperationClaims.Add(claim);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            _db.Entry(claim).State = EntityState.Detached;
            return false;
        }
        catch (InvalidOperationException)
        {
            _db.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    public async Task CompleteAsync(
        PaymentOperationClaim claim,
        string? referenceId,
        string? resultJson,
        CancellationToken cancellationToken)
    {
        claim.Complete(referenceId, resultJson);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(PaymentOperationClaim claim, CancellationToken cancellationToken)
    {
        _db.PaymentOperationClaims.Remove(claim);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private bool _released;

        public Releaser(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _gate.Release();
        }
    }
}
