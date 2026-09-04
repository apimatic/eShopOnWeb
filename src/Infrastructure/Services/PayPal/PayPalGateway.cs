using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public class PayPalGateway : IPayPalGateway
{
    private const int MaxReportingPageSize = 500;

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;

    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // PayPal's REST contract uses snake_case field names. Our request payloads and
        // response DTOs are declared with exactly those names, so no naming policy is
        // applied on serialize; case-insensitive matching covers deserialization.
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalGateway(HttpClient httpClient, PayPalOptions options, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(ResolveBaseUrl(options));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string ResolveBaseUrl(PayPalOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            return options.BaseUrl.TrimEnd('/') + "/";

        return options.Environment.ToLowerInvariant() switch
        {
            "sandbox" => "https://api-m.sandbox.paypal.com/",
            "live" or "production" => "https://api-m.paypal.com/",
            _ => throw new InvalidOperationException(
                $"Unsupported PayPal environment '{options.Environment}'. Use 'sandbox' or 'live'.")
        };
    }

    public async Task<PayPalAuthorizationResult> CreateOrderAndAuthorizeAsync(string customId, string invoiceId,
        decimal amount, string currency, PayPalCardSource cardSource, string requestId)
    {
        var payload = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = customId,
                    custom_id = customId,
                    invoice_id = invoiceId,
                    amount = new { currency_code = currency, value = Money(amount) }
                }
            },
            payment_source = BuildPaymentSource(cardSource)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v2/checkout/orders")
        {
            Content = ToJson(payload)
        };
        request.Headers.Add("PayPal-Request-Id", requestId);

        var response = await SendAsync<CreateOrderResponse>(request, CancellationToken.None);

        if (string.Equals(response.status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal requires a payer action (e.g. a 3-D Secure challenge) in a browser to complete this card " +
                "payment. The money was not authorized. No approval round-trip is performed by this integration.");
        }

        var authorization = response.purchase_units?.FirstOrDefault()?.payments?.authorizations?.FirstOrDefault();
        if (authorization?.id is null)
        {
            throw new PayPalApiException("NO_AUTHORIZATION",
                "PayPal did not return an authorization for the order.", HttpStatusCode.UnprocessableEntity,
                new[] { "NO_AUTHORIZATION" });
        }

        return new PayPalAuthorizationResult(response.id ?? string.Empty, authorization.id,
            authorization.status ?? string.Empty, ParseDate(authorization.expiration_time));
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount,
        string currency, string requestId)
    {
        var payload = new
        {
            amount = new { currency_code = currency, value = Money(amount) },
            final_capture = true
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/capture")
        {
            Content = ToJson(payload)
        };
        request.Headers.Add("PayPal-Request-Id", requestId);
        request.Headers.Add("Prefer", "return=representation");

        var response = await SendAsync<CaptureResponse>(request, CancellationToken.None);

        return new PayPalCaptureResult(response.id ?? string.Empty, response.status ?? string.Empty,
            ParseMoney(response.amount?.value), ParseMoneyNullable(response.seller_receivable_breakdown?.paypal_fee?.value),
            ParseMoneyNullable(response.seller_receivable_breakdown?.net_amount?.value),
            response.amount?.currency_code ?? currency);
    }

    public async Task<PayPalVoidResult> VoidAuthorizationAsync(string authorizationId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/void");
        request.Headers.Add("Prefer", "return=representation");

        var response = await SendAsync(request, CancellationToken.None);
        await EnsureSuccessAsync(response, CancellationToken.None);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return new PayPalVoidResult(authorizationId, "VOIDED");
        }

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        var dto = Deserialize<VoidResponse>(body);
        return new PayPalVoidResult(dto?.id ?? authorizationId, dto?.status ?? "VOIDED");
    }

    public async Task<PayPalReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency)
    {
        var payload = new
        {
            amount = new { currency_code = currency, value = Money(amount) }
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"v2/payments/authorizations/{authorizationId}/reauthorize")
        {
            Content = ToJson(payload)
        };
        request.Headers.Add("Prefer", "return=representation");

        var response = await SendAsync<ReauthorizeResponse>(request, CancellationToken.None);

        return new PayPalReauthorizeResult(response.id ?? string.Empty, response.status ?? string.Empty,
            ParseDate(response.expiration_time));
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string requestId)
    {
        object payload;
        if (amount.HasValue)
        {
            payload = new
            {
                amount = new { currency_code = currency, value = Money(amount.Value) }
            };
        }
        else
        {
            payload = new { };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"v2/payments/captures/{captureId}/refund")
        {
            Content = ToJson(payload)
        };
        request.Headers.Add("PayPal-Request-Id", requestId);
        request.Headers.Add("Prefer", "return=representation");

        var response = await SendAsync<RefundResponse>(request, CancellationToken.None);

        return new PayPalRefundResult(response.id ?? string.Empty, response.status ?? string.Empty,
            ParseMoney(response.amount?.value), response.amount?.currency_code ?? currency);
    }

    public async Task<PayPalSetupTokenResult> CreateSetupTokenAsync(CardDetails card, string merchantCustomerId, string requestId)
    {
        var payload = new
        {
            customer = new { merchant_customer_id = merchantCustomerId },
            payment_source = new
            {
                card = new
                {
                    number = card.Number,
                    expiry = card.Expiry,
                    name = card.Name,
                    security_code = card.SecurityCode,
                    billing_address = card.BillingAddress == null ? null : new
                    {
                        address_line_1 = card.BillingAddress.AddressLine1,
                        address_line_2 = card.BillingAddress.AddressLine2,
                        admin_area_1 = card.BillingAddress.AdminArea1,
                        admin_area_2 = card.BillingAddress.AdminArea2,
                        postal_code = card.BillingAddress.PostalCode,
                        country_code = card.BillingAddress.CountryCode
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/vault/setup-tokens")
        {
            Content = ToJson(payload)
        };
        request.Headers.Add("PayPal-Request-Id", requestId);

        var response = await SendAsync<SetupTokenResponse>(request, CancellationToken.None);

        return new PayPalSetupTokenResult(response.id ?? string.Empty, response.customer?.id ?? string.Empty,
            response.status ?? string.Empty);
    }

    public async Task<PayPalPaymentTokenResult> CreatePaymentTokenAsync(string setupTokenId, string requestId)
    {
        var payload = new
        {
            payment_source = new
            {
                token = new
                {
                    id = setupTokenId,
                    type = "SETUP_TOKEN"
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/vault/payment-tokens")
        {
            Content = ToJson(payload)
        };
        request.Headers.Add("PayPal-Request-Id", requestId);

        var response = await SendAsync<PaymentTokenResponse>(request, CancellationToken.None);

        var card = response.payment_source?.card;
        return new PayPalPaymentTokenResult(response.id ?? string.Empty, response.customer?.id ?? string.Empty,
            card?.last_digits ?? string.Empty, card?.brand ?? string.Empty, card?.expiry ?? string.Empty,
            card?.name ?? string.Empty);
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"v3/vault/payment-tokens/{paymentTokenId}");
        var response = await SendAsync(request, CancellationToken.None);

        // 404 means the token is already gone; deleting our local row remains correct.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;

        await EnsureSuccessAsync(response, CancellationToken.None);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var transactions = new List<PayPalTransaction>();
        var page = 1;
        var totalPages = 1;

        do
        {
            var query = $"v1/reporting/transactions?start_date={ReportDate(from)}&end_date={ReportDate(to)}" +
                        $"&fields=all&page_size={MaxReportingPageSize}&page={page}";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            var response = await SendAsync<SearchTransactionsResponse>(request, CancellationToken.None);

            if (response.transaction_details is not null)
            {
                foreach (var detail in response.transaction_details)
                {
                    var info = detail.transaction_info;
                    if (info is null) continue;

                    transactions.Add(new PayPalTransaction(
                        info.transaction_id ?? string.Empty,
                        info.transaction_event_code ?? string.Empty,
                        info.transaction_status ?? string.Empty,
                        ParseDate(info.transaction_initiation_date) ?? from,
                        info.custom_field,
                        info.invoice_id,
                        ParseMoneyNullable(info.transaction_amount?.value),
                        ParseMoneyNullable(info.fee_amount?.value),
                        info.transaction_amount?.currency_code,
                        info.paypal_reference_id,
                        info.paypal_reference_id_type));
                }
            }

            totalPages = Math.Max(response.total_pages, 1);
            page++;
        }
        while (page <= totalPages);

        return transactions;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return _accessToken;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return _accessToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                })
            };

            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var token = Deserialize<AccessTokenDto>(body);

            _accessToken = token?.access_token;
            var expiresInSeconds = token?.expires_in ?? 28800;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresInSeconds - 60, 60));

            return _accessToken ?? throw new PayPalApiException("INVALID_TOKEN",
                "PayPal returned an empty access token.", HttpStatusCode.BadGateway, Array.Empty<string>());
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken) where T : class
    {
        var response = await SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
            return null!;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return Deserialize<T>(body) ?? throw new PayPalApiException("INVALID_RESPONSE",
            "PayPal returned an unreadable response.", HttpStatusCode.BadGateway, Array.Empty<string>());
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        PayPalErrorDto? error = null;
        try
        {
            error = Deserialize<PayPalErrorDto>(body);
        }
        catch (JsonException)
        {
            // fall through to the generic message
        }

        var issues = error?.details?.Select(d => d.issue).Where(i => !string.IsNullOrEmpty(i)).Cast<string>().ToList()
                     ?? new List<string>();
        var message = !string.IsNullOrEmpty(error?.message)
            ? (issues.Count > 0
                ? $"{error!.message} Issues: {string.Join(", ", issues)}"
                : error!.message!)
            : $"PayPal returned HTTP {(int)response.StatusCode}.";

        throw new PayPalApiException(error?.name ?? "PAYPAL_ERROR", message, response.StatusCode, issues);
    }

    private static object BuildPaymentSource(PayPalCardSource source)
    {
        if (source.IsSavedCard)
        {
            return new
            {
                card = new
                {
                    vault_id = source.VaultId,
                    stored_credential = new
                    {
                        payment_initiator = "MERCHANT",
                        payment_type = "UNSCHEDULED",
                        usage = "SUBSEQUENT"
                    }
                }
            };
        }

        var card = source.Card!;
        return new
        {
            card = new
            {
                number = card.Number,
                expiry = card.Expiry,
                name = card.Name,
                security_code = card.SecurityCode,
                billing_address = card.BillingAddress == null ? null : new
                {
                    address_line_1 = card.BillingAddress.AddressLine1,
                    address_line_2 = card.BillingAddress.AddressLine2,
                    admin_area_1 = card.BillingAddress.AdminArea1,
                    admin_area_2 = card.BillingAddress.AdminArea2,
                    postal_code = card.BillingAddress.PostalCode,
                    country_code = card.BillingAddress.CountryCode
                }
            }
        };
    }

    private static StringContent ToJson(object payload) =>
        new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

    private static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions);

    private static string Money(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;

    private static decimal? ParseMoneyNullable(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string ReportDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}