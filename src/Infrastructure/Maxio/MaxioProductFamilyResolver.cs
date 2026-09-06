using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Resolves the configured product family handle to the numeric id the product-listing operation
/// requires, and caches it.
/// </summary>
/// <remarks>
/// The read-a-family operation takes an <c>int</c>, so a family cannot be fetched by handle through
/// this SDK; families are listed and matched on <c>Handle</c> instead. Numeric ids are re-assigned
/// when a Maxio site is re-seeded, which is exactly why none is ever configured or hard-coded — the
/// handle is the input and the id is derived, cached briefly, and re-derived after that.
/// </remarks>
public sealed class MaxioProductFamilyResolver
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedHandle;
    private string? _cachedFamilyId;
    private DateTimeOffset _cacheExpiresAt;

    public MaxioProductFamilyResolver(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    /// <summary>Returns the numeric family id, as the string the products operation expects.</summary>
    public async Task<string> ResolveFamilyIdAsync(CancellationToken cancellationToken)
    {
        var handle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw MaxioFailures.NotConfigured();
        }

        if (TryReadCache(handle!, out var cached))
        {
            return cached!;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (TryReadCache(handle!, out cached))
            {
                return cached!;
            }

            var familyId = await LookUpFamilyIdAsync(handle!, cancellationToken);

            _cachedHandle = handle;
            _cachedFamilyId = familyId;
            _cacheExpiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _settings.CatalogCacheMinutes));

            return familyId;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryReadCache(string handle, out string? familyId)
    {
        familyId = _cachedFamilyId;
        return familyId is not null
            && string.Equals(_cachedHandle, handle, StringComparison.Ordinal)
            && DateTimeOffset.UtcNow < _cacheExpiresAt;
    }

    private async Task<string> LookUpFamilyIdAsync(string handle, CancellationToken cancellationToken)
    {
        const string operation = "listing product families";

        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken);

            var match = families
                .Select(response => response.ProductFamily)
                .FirstOrDefault(family => family is not null
                    && string.Equals(family.Handle, handle, StringComparison.OrdinalIgnoreCase));

            if (match?.Id is null)
            {
                throw new SubscriptionBillingException(
                    ApplicationCore.Billing.BillingFailureKind.NotConfigured,
                    $"No Maxio product family with handle '{handle}' exists on the configured site.");
            }

            return match.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
}
