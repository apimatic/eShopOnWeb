using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Decides how a new subscription's balance is collected, and caches the answer.
/// </summary>
/// <remarks>
/// <para>
/// Omitting the collection method makes Maxio try to charge a card at signup, which fails with
/// "No payment method was on file for the $… balance" for a plan that has a balance due and a
/// shopper who has none. Invoicing the balance instead is what lets this flow work without card
/// capture or 3-DS.
/// </para>
/// <para>
/// Which member means "invoice it" depends on the site's billing architecture — <c>remittance</c>
/// under Relationship Invoicing, <c>invoice</c> under the legacy Statements architecture — so it is
/// read from the site rather than assumed. An operator can override the choice through
/// <see cref="MaxioSettings.PaymentCollectionMethod"/>, which also skips the lookup.
/// </para>
/// </remarks>
public sealed class MaxioSiteResolver
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CollectionMethod? _cached;
    private DateTimeOffset _cacheExpiresAt;

    public MaxioSiteResolver(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<CollectionMethod> ResolveCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var configured = ParseCollectionMethod(_settings.PaymentCollectionMethod);
        if (configured is not null)
        {
            return configured;
        }

        if (_cached is not null && DateTimeOffset.UtcNow < _cacheExpiresAt)
        {
            return _cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow < _cacheExpiresAt)
            {
                return _cached;
            }

            var method = await ReadSiteCollectionMethodAsync(cancellationToken);

            _cached = method;
            _cacheExpiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _settings.CatalogCacheMinutes));

            return method;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CollectionMethod> ReadSiteCollectionMethodAsync(CancellationToken cancellationToken)
    {
        const string operation = "reading the Maxio site configuration";

        try
        {
            var response = await _client.Sites.ReadSite(ct: cancellationToken);

            return response.Site.RelationshipInvoicingEnabled == true
                ? CollectionMethod.Remittance
                : CollectionMethod.Invoice;
        }
        catch (SdkException<RawError> ex)
        {
            throw MaxioFailures.FromRawError(ex.Error, operation, ex);
        }
        catch (JsonException ex)
        {
            throw MaxioFailures.UnreadableResponse(operation, ex);
        }
        catch (Exception ex) when (MaxioFailures.IsTransportFailure(ex))
        {
            throw MaxioFailures.Unavailable(operation, ex);
        }
    }

    /// <summary>
    /// Maps the configured override onto the SDK's enum. Written out rather than routed through a
    /// value-parsing helper, so an unrecognised setting is a clear configuration error instead of a
    /// string that reaches Maxio and is rejected there.
    /// </summary>
    internal static CollectionMethod? ParseCollectionMethod(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        switch (configured!.Trim().ToLowerInvariant())
        {
            case "remittance":
                return CollectionMethod.Remittance;
            case "invoice":
                return CollectionMethod.Invoice;
            case "automatic":
                return CollectionMethod.Automatic;
            case "prepaid":
                return CollectionMethod.Prepaid;
            default:
                throw MaxioFailures.NotConfigured(
                    $"'{configured}' is not a valid Maxio:PaymentCollectionMethod; use remittance, invoice, automatic or prepaid.");
        }
    }
}
