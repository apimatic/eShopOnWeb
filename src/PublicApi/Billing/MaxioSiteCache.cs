using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public record MaxioSiteInfo(bool RelationshipInvoicing, IReadOnlyList<string> NonPrimaryCurrencies, string? PrimaryCurrency);

/// <summary>Site facts (invoice architecture, currencies), read from Maxio and cached briefly.</summary>
public class MaxioSiteCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly IHostEnvironment _env;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MaxioSiteInfo? _info;
    private DateTimeOffset _readAt;

    public MaxioSiteCache(IHostEnvironment env)
    {
        _env = env;
    }

    public async Task<MaxioSiteInfo> GetAsync(MaxioGateway gateway, CancellationToken ct, bool forceRefresh = false)
    {
        if (!forceRefresh && _info != null && DateTimeOffset.UtcNow - _readAt < Lifetime) return _info;
        await _gate.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _info != null && DateTimeOffset.UtcNow - _readAt < Lifetime) return _info;
            var site = (await gateway.ReadAsync(c => c.Sites.ReadSite(cancellationToken: ct))).Site;
            // Outside production the app only talks to a Maxio test-mode site.
            if (!_env.IsProduction() && site.Test != true)
                throw new BillingException(503, "Billing is configured against a live Maxio site; refusing to use it outside production.");
            _info = new MaxioSiteInfo(
                site.RelationshipInvoicingEnabled == true,
                (site.NonPrimaryCurrencies ?? []).Select(c => c.ToUpperInvariant()).ToList(),
                site.Currency);
            _readAt = DateTimeOffset.UtcNow;
            return _info;
        }
        finally
        {
            _gate.Release();
        }
    }
}
