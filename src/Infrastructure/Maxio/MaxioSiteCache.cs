using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The site-level facts the subscription flow needs, and which the per-plan and per-subscription
/// payloads do not carry: the site currency and whether the site runs Relationship Invoicing.
/// </summary>
/// <param name="Currency">The site's primary currency, e.g. <c>USD</c>. Null when it is unknown.</param>
/// <param name="RelationshipInvoicingEnabled">
/// Whether the site uses the Relationship Invoicing architecture. This decides which collection
/// methods are valid: <c>remittance</c> under Relationship Invoicing, <c>invoice</c> on legacy
/// Statements sites (see <c>components/schemas/Collection-Method.yaml</c>).
/// </param>
public record MaxioSiteInfo(string? Currency, bool? RelationshipInvoicingEnabled);

/// <summary>
/// Caches <see cref="MaxioSiteInfo"/>. Plans carry a price in cents but no currency of their own -
/// currency and architecture belong to the site (<c>GET /site.json</c>) and effectively never change,
/// so they are fetched once and reused rather than on every call.
/// </summary>
/// <remarks>
/// Registered as a singleton, but deliberately takes the client per call: a typed
/// <see cref="System.Net.Http.HttpClient"/> must not be captured for the lifetime of the process.
/// </remarks>
public sealed class MaxioSiteCache
{
    /// <summary>How long a failed lookup is remembered, so a broken site call cannot be hammered.</summary>
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(1);

    private static readonly MaxioSiteInfo Unknown = new(null, null);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<MaxioSiteCache> _logger;

    private MaxioSiteInfo _site = Unknown;

    /// <summary>Expiry as ticks so it can be read and written atomically from any thread.</summary>
    private long _expiresAtTicks = DateTimeOffset.MinValue.UtcTicks;

    public MaxioSiteCache(ILogger<MaxioSiteCache> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns what is known about the site. Never throws: neither the currency nor the architecture
    /// is essential, so a failure here must not be able to fail a plan listing or a signup.
    /// </summary>
    public async Task<MaxioSiteInfo> GetAsync(IMaxioApiClient client, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        if (!HasExpired())
        {
            return Volatile.Read(ref _site);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!HasExpired())
            {
                return Volatile.Read(ref _site);
            }

            MaxioSiteInfo site;
            TimeSpan validFor;
            try
            {
                var response = await client.ReadSiteAsync(cancellationToken);
                site = Map(response.Site);
                validFor = site == Unknown ? FailureBackoff : timeToLive;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not read the Maxio site; falling back to defaults for currency and collection method.");
                site = Unknown;
                validFor = FailureBackoff;
            }

            Volatile.Write(ref _site, site);
            Volatile.Write(ref _expiresAtTicks, DateTimeOffset.UtcNow.Add(validFor).UtcTicks);

            return site;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool HasExpired() => DateTimeOffset.UtcNow.UtcTicks >= Volatile.Read(ref _expiresAtTicks);

    private static MaxioSiteInfo Map(MaxioSite? site) =>
        site is null ? Unknown : new MaxioSiteInfo(site.Currency, site.RelationshipInvoicingEnabled);
}
