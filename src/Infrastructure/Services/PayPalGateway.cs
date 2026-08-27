using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// PayPal REST implementation of <see cref="IPayPalGateway"/> covering
/// Orders v2 (authorize), Payments v2 (capture/reauthorize/void/refund),
/// Vault v3 (payment method tokens) and Transaction Search v1.
///
/// Security note: request payloads may contain full card details in transit.
/// They are never logged and never persisted; only safe attributes returned
/// by PayPal (brand, last digits) leave this class.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalSettings> settings, IAppLogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        // BaseUrl (when configured) is used verbatim for every PayPal call,
        // including the token request; otherwise it derives from Environment.
        _httpClient.BaseAddress = new Uri(_settings.EffectiveBaseUrl + "/");
    }

    public async Task<GatewayAuthorization> AuthorizeCardAsync(decimal amount, string currency, CardDetails card,
        string referenceId, string requestId, CancellationToken cancellationToken = default)
    {
        var paymentSource = new Dictionary<string, object?>
        {
            ["card"] = BuildCardPayload(card)
        };
        return await CreateAndAuthorizeOrderAsync(amount, currency, paymentSource, referenceId, requestId, cancellationToken);
    }

    public async Task<GatewayAuthorization> AuthorizeVaultedCardAsync(decimal amount, string currency, string vaultTokenId,
        string referenceId, string requestId, CancellationToken cancellationToken = default)
    {
        var paymentSource = new Dictionary<string, object?>
        {
            ["card"] = new Dictionary<string, object?>
            {
                ["vault_id"] = vaultTokenId
            }
        };
        return await CreateAndAuthorizeOrderAsync(amount, currency, paymentSource, referenceId, requestId, cancellationToken);
    }

    public async Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = MoneyPayload(amount, currency),
            ["final_capture"] = true
        };

        var capture = await SendAsync<PayPalCapture>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture", body, requestId, cancellationToken);

        return new GatewayCapture
        {
            CaptureId = capture?.Id ?? string.Empty,
            Status = capture?.Status ?? string.Empty,
            GrossAmount = ParseMoney(capture?.SellerReceivableBreakdown?.GrossAmount ?? capture?.Amount),
            PayPalFee = ParseNullableMoney(capture?.SellerReceivableBreakdown?.PayPalFee),
            NetAmount = ParseNullableMoney(capture?.SellerReceivableBreakdown?.NetAmount),
            Currency = capture?.SellerReceivableBreakdown?.GrossAmount?.CurrencyCode
                ?? capture?.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = MoneyPayload(amount, currency)
        };

        var authorization = await SendAsync<PayPalAuthorization>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId, cancellationToken);

        return MapAuthorization(string.Empty, authorization);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void", null, null, cancellationToken);
    }

    public async Task<GatewayRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string requestId, CancellationToken cancellationToken = default)
    {
        // An empty payload refunds the full captured amount; an amount object
        // makes it a partial refund.
        var body = amount.HasValue
            ? new Dictionary<string, object?> { ["amount"] = MoneyPayload(amount.Value, currency) }
            : new Dictionary<string, object?>();

        var refund = await SendAsync<PayPalRefundResponse>(HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund", body, requestId, cancellationToken);

        return new GatewayRefundResult
        {
            RefundId = refund?.Id ?? string.Empty,
            Status = refund?.Status ?? string.Empty,
            Amount = ParseMoney(refund?.Amount),
            Currency = refund?.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string? payPalCustomerId,
        string requestId, CancellationToken cancellationToken = default)
    {
        var setupBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildCardPayload(card)
            }
        };
        if (!string.IsNullOrEmpty(payPalCustomerId))
        {
            setupBody["customer"] = new Dictionary<string, object?> { ["id"] = payPalCustomerId };
        }

        var setupToken = await SendAsync<PayPalSetupTokenResponse>(HttpMethod.Post,
            "v3/vault/setup-tokens", setupBody, requestId + "-setup", cancellationToken);

        if (string.IsNullOrEmpty(setupToken?.Id))
        {
            throw new PaymentGatewayException("PayPal did not return a setup token for the card.");
        }

        var paymentTokenBody = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?>
                {
                    ["id"] = setupToken.Id,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };

        var paymentToken = await SendAsync<PayPalPaymentTokenResponse>(HttpMethod.Post,
            "v3/vault/payment-tokens", paymentTokenBody, requestId, cancellationToken);

        if (string.IsNullOrEmpty(paymentToken?.Id))
        {
            throw new PaymentGatewayException("PayPal did not return a payment token for the card.");
        }

        return new GatewayVaultedCard
        {
            VaultTokenId = paymentToken.Id,
            CustomerId = paymentToken.Customer?.Id ?? setupToken.Customer?.Id,
            Brand = paymentToken.PaymentSource?.Card?.Brand,
            Last4 = paymentToken.PaymentSource?.Card?.LastDigits,
            Expiry = paymentToken.PaymentSource?.Card?.Expiry
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<object>(HttpMethod.Delete,
                $"v3/vault/payment-tokens/{vaultTokenId}", null, null, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.HttpStatusCode == (int)HttpStatusCode.NotFound)
        {
            // Already gone from the PayPal vault; deletion is idempotent.
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();
        const int pageSize = 500; // PayPal maximum; walk every page of the range.
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            var query = $"v1/reporting/transactions" +
                $"?start_date={Uri.EscapeDataString(FormatInstant(from))}" +
                $"&end_date={Uri.EscapeDataString(FormatInstant(to))}" +
                $"&fields=transaction_info&page_size={pageSize}&page={page}";

            var response = await SendAsync<PayPalTransactionSearchResponse>(HttpMethod.Get,
                query, null, null, cancellationToken);

            if (response?.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;
                    results.Add(new GatewayTransaction
                    {
                        TransactionId = info.TransactionId ?? string.Empty,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        Amount = ParseNullableMoney(info.TransactionAmount),
                        Currency = info.TransactionAmount?.CurrencyCode,
                        Time = ParseDate(info.TransactionInitiationDate)
                    });
                }
            }

            totalPages = response?.TotalPages ?? 1;
            page++;
        }

        return results;
    }

    private async Task<GatewayAuthorization> CreateAndAuthorizeOrderAsync(decimal amount, string currency,
        Dictionary<string, object?> paymentSource, string referenceId, string requestId,
        CancellationToken cancellationToken)
    {
        var createBody = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["reference_id"] = referenceId,
                    ["amount"] = MoneyPayload(amount, currency)
                }
            },
            ["payment_source"] = paymentSource
        };

        var order = await SendAsync<PayPalOrderResponse>(HttpMethod.Post,
            "v2/checkout/orders", createBody, requestId, cancellationToken);

        if (string.IsNullOrEmpty(order?.Id))
        {
            throw new PaymentGatewayException("PayPal did not return an order id.");
        }

        // When the payment source is supplied at create time, PayPal authorizes
        // immediately and the create response already carries the authorization.
        var existingAuthorization = GetFirstAuthorization(order);
        if (existingAuthorization?.Id is not null)
        {
            return MapAuthorization(order.Id, existingAuthorization);
        }

        var authorized = await SendAsync<PayPalOrderResponse>(HttpMethod.Post,
            $"v2/checkout/orders/{order.Id}/authorize",
            new Dictionary<string, object?>(), requestId + "-auth", cancellationToken);

        if (string.Equals(authorized?.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException(
                "PayPal requires the shopper to approve this payment in a browser (3D Secure), " +
                "which this integration does not support.", "PAYER_ACTION_REQUIRED");
        }

        var authorization = GetFirstAuthorization(authorized);
        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException(
                $"PayPal authorization did not complete (order status: {authorized?.Status ?? "unknown"}).",
                authorized?.Status);
        }

        return MapAuthorization(order.Id, authorization);
    }

    private static PayPalAuthorization? GetFirstAuthorization(PayPalOrderResponse? order)
    {
        if (order?.PurchaseUnits is null) return null;
        foreach (var unit in order.PurchaseUnits)
        {
            if (unit.Payments?.Authorizations is { Count: > 0 })
            {
                return unit.Payments.Authorizations[0];
            }
        }
        return null;
    }

    private static GatewayAuthorization MapAuthorization(string payPalOrderId, PayPalAuthorization? authorization)
    {
        return new GatewayAuthorization
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authorization?.Id ?? string.Empty,
            Status = authorization?.Status ?? string.Empty,
            Amount = ParseMoney(authorization?.Amount),
            Currency = authorization?.Amount?.CurrencyCode ?? string.Empty,
            CreatedAt = ParseDate(authorization?.CreateTime) ?? DateTimeOffset.UtcNow,
            ExpiresAt = ParseDate(authorization?.ExpirationTime)
        };
    }

    private static Dictionary<string, object?> BuildCardPayload(CardDetails card)
    {
        var payload = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };
        if (!string.IsNullOrEmpty(card.SecurityCode)) payload["security_code"] = card.SecurityCode;
        if (!string.IsNullOrEmpty(card.Name)) payload["name"] = card.Name;

        var address = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(card.AddressLine1)) address["address_line_1"] = card.AddressLine1;
        if (!string.IsNullOrEmpty(card.AdminArea2)) address["admin_area_2"] = card.AdminArea2;
        if (!string.IsNullOrEmpty(card.AdminArea1)) address["admin_area_1"] = card.AdminArea1;
        if (!string.IsNullOrEmpty(card.PostalCode)) address["postal_code"] = card.PostalCode;
        if (!string.IsNullOrEmpty(card.CountryCode)) address["country_code"] = card.CountryCode;
        if (address.Count > 0) payload["billing_address"] = address;

        return payload;
    }

    private static Dictionary<string, object?> MoneyPayload(decimal amount, string currency)
    {
        return new Dictionary<string, object?>
        {
            ["currency_code"] = currency,
            ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
        };
    }

    private static decimal ParseMoney(PayPalMoney? money)
    {
        return ParseNullableMoney(money) ?? 0m;
    }

    private static decimal? ParseNullableMoney(PayPalMoney? money)
    {
        if (money?.Value is null) return null;
        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (value is null) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatInstant(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                })
            };
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"PayPal token request failed with status {(int)response.StatusCode}.");
                throw new PaymentGatewayException(
                    "Could not authenticate with PayPal; check the configured client credentials.",
                    httpStatusCode: (int)response.StatusCode);
            }

            var token = await response.Content.ReadFromJsonAsync<PayPalTokenResponse>(JsonOptions, cancellationToken);
            if (token?.AccessToken is null)
            {
                throw new PaymentGatewayException("PayPal did not return an access token.");
            }

            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId,
        CancellationToken cancellationToken, bool retryOnUnauthorized = true)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Without this PayPal returns minimal representations that omit the
        // amounts and the seller receivable breakdown (fee/net).
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (requestId is not null)
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }
        else if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && retryOnUnauthorized)
        {
            _accessToken = null;
            _tokenExpiresAt = DateTimeOffset.MinValue;
            return await SendAsync<T>(method, path, body, requestId, cancellationToken, retryOnUnauthorized: false);
        }

        if (!response.IsSuccessStatusCode)
        {
            // Never log the request or response payload here: requests can
            // carry card details. Only the status and PayPal error name.
            string? errorName = null;
            string? errorMessage = null;
            try
            {
                var error = await response.Content.ReadFromJsonAsync<PayPalErrorResponse>(JsonOptions, cancellationToken);
                errorName = error?.Name;
                errorMessage = error?.Message;
                var detail = error?.Details?.FirstOrDefault();
                if (detail?.Issue is not null)
                {
                    errorMessage = $"{errorMessage} [{detail.Issue}: {detail.Description}]";
                }
            }
            catch (JsonException)
            {
                // Body was not a PayPal error payload; the status code is enough.
            }

            _logger.LogWarning(
                $"PayPal {method} {path} failed with status {(int)response.StatusCode} ({errorName ?? "no error name"}).");
            throw new PaymentGatewayException(
                $"PayPal {method} {path} failed: {errorMessage ?? response.ReasonPhrase ?? "unknown error"}",
                errorName, (int)response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object))
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }
}
