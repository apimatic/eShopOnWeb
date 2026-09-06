using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Caches the rarely changing site facts (currently the site currency, read via <c>readSite</c>) so
/// listing plans costs one Maxio call instead of two.
/// </summary>
/// <remarks>
/// Site metadata is presentation detail, so a failure to read it degrades the response rather than
/// failing the request. The fetch is supplied by the caller, which keeps this singleton free of any
/// long-lived <c>HttpClient</c>.
/// </remarks>
public sealed class MaxioSiteMetadataCache
{
    private readonly IOptions<MaxioOptions> _options;
    private readonly ILogger<MaxioSiteMetadataCache> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _currency;

    /// <summary>Expiry as UTC ticks so the fast-path read outside the lock cannot tear.</summary>
    private long _expiresAtTicks = DateTime.MinValue.Ticks;

    public MaxioSiteMetadataCache(IOptions<MaxioOptions> options, ILogger<MaxioSiteMetadataCache> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>Returns the site currency, or <c>null</c> when it is not currently known.</summary>
    public async Task<string?> GetCurrencyAsync(
        Func<CancellationToken, Task<MaxioSite?>> readSite, CancellationToken cancellationToken = default)
    {
        if (!HasExpired())
        {
            return _currency;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!HasExpired())
            {
                return _currency;
            }

            try
            {
                var site = await readSite(cancellationToken).ConfigureAwait(false);

                _currency = string.IsNullOrWhiteSpace(site?.Currency) ? null : site.Currency;
                ExpireIn(TimeSpan.FromMinutes(Math.Max(1, _options.Value.SiteCacheMinutes)));
            }
            catch (Exception ex) when (ex is MaxioApiException or MaxioTransportException)
            {
                _logger.LogWarning(ex, "Could not read Maxio site metadata; plan prices will omit the currency.");

                // Back off briefly rather than hammering a failing endpoint on every plan listing.
                ExpireIn(TimeSpan.FromMinutes(1));
            }

            return _currency;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool HasExpired() => DateTime.UtcNow.Ticks >= Interlocked.Read(ref _expiresAtTicks);

    private void ExpireIn(TimeSpan lifetime) =>
        Interlocked.Exchange(ref _expiresAtTicks, DateTime.UtcNow.Add(lifetime).Ticks);
}
