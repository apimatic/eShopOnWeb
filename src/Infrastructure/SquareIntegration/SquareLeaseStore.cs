using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// Store-enforced claims for operator actions. A claim is a row whose primary key is the action name:
/// the database refuses a second insert, so two requests (or two instances) cannot both proceed.
/// Leases expire so a crashed holder never blocks the action forever.
/// </summary>
public sealed class SquareLeaseStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<SquareLeaseStore> _logger;

    public SquareLeaseStore(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<SquareLeaseStore> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Takes the claim, or returns null when someone else holds an unexpired one.</summary>
    public async Task<SquareLeaseHandle?> TryAcquireAsync(string name, TimeSpan duration, CancellationToken cancellationToken)
    {
        var owner = Guid.NewGuid().ToString("N");
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var now = _clock.GetUtcNow();

        var existing = await db.SquareLeases.SingleOrDefaultAsync(l => l.Name == name, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ExpiresAt > now)
            {
                return null;
            }

            // Take over an expired lease: delete it first; a concurrent taker makes this delete fail.
            db.SquareLeases.Remove(existing);
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return null;
            }

            _logger.LogWarning("Took over expired Square lease {Lease}", name);
        }

        db.SquareLeases.Add(new SquareLease { Name = name, Owner = owner, ExpiresAt = now + duration });
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            return null;
        }

        return new SquareLeaseHandle(this, name, owner);
    }

    internal async Task ReleaseAsync(string name, string owner)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
            var lease = await db.SquareLeases.SingleOrDefaultAsync(l => l.Name == name).ConfigureAwait(false);
            if (lease is not null && lease.Owner == owner)
            {
                db.SquareLeases.Remove(lease);
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // The lease expires on its own; failing to release early must not fail the request.
            _logger.LogWarning(ex, "Could not release Square lease {Lease}", name);
        }
    }

    /// <summary>
    /// A primary-key violation: SQL Server reports it as <see cref="DbUpdateException"/>; the in-memory
    /// provider (used in development and tests) raises <see cref="ArgumentException"/> or
    /// <see cref="InvalidOperationException"/> for the same key.
    /// </summary>
    public static bool IsDuplicateKey(Exception ex) =>
        ex is DbUpdateException and not DbUpdateConcurrencyException
        || ex is ArgumentException { Message: var m } && m.Contains("same key", StringComparison.OrdinalIgnoreCase)
        || ex is InvalidOperationException { Message: var m2 } && m2.Contains("same key", StringComparison.OrdinalIgnoreCase);
}

public sealed class SquareLeaseHandle : IAsyncDisposable
{
    private readonly SquareLeaseStore _store;
    private readonly string _name;
    private readonly string _owner;

    internal SquareLeaseHandle(SquareLeaseStore store, string name, string owner)
    {
        _store = store;
        _name = name;
        _owner = owner;
    }

    public ValueTask DisposeAsync() => new(_store.ReleaseAsync(_name, _owner));
}
