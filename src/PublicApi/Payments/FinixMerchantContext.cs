using System;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Requests.MerchantsApi;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Resolves and caches the merchant's Finix Identity, which payment instruments are created under.
/// </summary>
public sealed class FinixMerchantContext
{
    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _identityId;
    private bool _currencyChecked;

    public FinixMerchantContext(FinixApiReferenceClient client, IOptions<FinixOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<string> GetIdentityAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_identityId))
        {
            return _identityId;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_identityId))
            {
                return _identityId;
            }

            var merchant = await _client.MerchantsApi.GetMerchant(
                new GetMerchantRequest { MerchantId = _options.MerchantId },
                cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(merchant.Identity))
            {
                throw new InvalidOperationException(
                    $"Finix merchant {_options.MerchantId} does not expose an Identity; " +
                    "a payment instrument cannot be created under it.");
            }

            _identityId = merchant.Identity;
            return _identityId;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task EnsureCurrencySupportedAsync(CancellationToken cancellationToken)
    {
        if (_currencyChecked)
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_currencyChecked)
            {
                return;
            }

            var merchant = await _client.MerchantsApi.GetMerchant(
                new GetMerchantRequest { MerchantId = _options.MerchantId },
                cancellationToken: cancellationToken);

            if (merchant.Currencies is { Count: > 0 })
            {
                var wanted = _options.CurrencyCode;
                var supported = false;
                foreach (var currency in merchant.Currencies)
                {
                    if (string.Equals(currency.Value, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        supported = true;
                        break;
                    }
                }

                if (!supported)
                {
                    throw new InvalidOperationException(
                        $"Finix merchant {_options.MerchantId} does not support {wanted}.");
                }
            }

            _currencyChecked = true;
        }
        finally
        {
            _lock.Release();
        }
    }
}
