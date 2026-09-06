using System;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Reads and caches the site-level facts the integration depends on.
/// </summary>
/// <remarks>
/// Two things come from here. The currency, because a product carries none of its own and plan prices have
/// to be labelled somehow; and the billing architecture, because it decides whether the provider accepts
/// <c>remittance</c> or <c>invoice</c> as the collection method. Neither changes in practice, so one call
/// is cached and shared no matter how many callers ask at once.
/// </remarks>
public sealed class MaxioSiteProvider
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSiteProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private MaxioSiteInfo? _site;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public MaxioSiteProvider(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSiteProvider> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the cached site facts, or <c>null</c> when the site could not be read.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception, because the two callers want opposite things from a failure: listing
    /// plans should still succeed (just without a currency label), while subscribing must not proceed
    /// without knowing how the provider expects to be paid.
    /// </remarks>
    public async Task<MaxioSiteInfo?> GetSiteAsync(CancellationToken cancellationToken)
    {
        var cached = Volatile.Read(ref _site);
        if (cached is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return cached;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            cached = Volatile.Read(ref _site);
            if (cached is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return cached;
            }

            var response = await _client.Sites.ReadSite(ct: cancellationToken);

            var site = new MaxioSiteInfo(
                response.Site.Currency,
                response.Site.RelationshipInvoicingEnabled ?? false,
                response.Site.Test);

            Volatile.Write(ref _site, site);
            _expiresAt = DateTimeOffset.UtcNow.Add(_settings.SiteCacheDuration);

            if (site.IsTestSite == false)
            {
                _logger.LogWarning(
                    "Maxio site '{Subdomain}' is not a test site; subscription calls affect live billing data.",
                    response.Site.Subdomain);
            }

            return site;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Deliberately not cached: the next caller gets a fresh attempt rather than inheriting a failure.
            _logger.LogWarning(ex, "Could not read the Maxio site.");
            return null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
