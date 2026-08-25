using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Talks to the PayPal REST API (Orders v2, Payments v2, Vault v3, Reporting v1) over plain HTTP.
/// Endpoints, fields and status semantics were confirmed against developer.paypal.com/docs before
/// this was written - see the accompanying integration notes.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedAccessToken;
    private DateTimeOffset _cachedAccessTokenExpiry = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient httpClient, PayPalOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        decimal amount, string currencyCode, string invoiceId, string payPalRequestId,
        PayPalCardDetails? card, string? vaultId, CancellationToken ct = default)
    {
        if ((card is null) == (vaultId is null))
        {
            throw new ArgumentException("Exactly one of card or vaultId must be supplied.");
        }

        var cardSource = vaultId is not null
            ? new CardPaymentSourceRequest { VaultId = vaultId }
            : BuildCardPaymentSourceRequest(card!);

        var request = new CreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    InvoiceId = invoiceId,
                    Amount = new MoneyDto { CurrencyCode = currencyCode, Value = FormatAmount(amount) }
                }
            },
            PaymentSource = new PaymentSourceRequest { Card = cardSource }
        };

        var response = await SendJsonAsync(HttpMethod.Post, "/v2/checkout/orders", request, payPalRequestId, ct);
        var order = await ReadOrThrowAsync<OrderResponse>(response, ct);

        var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        var requiresPayerAction = string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase);

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = order.Id,
            OrderStatus = order.Status,
            RequiresPayerAction = requiresPayerAction,
            AuthorizationId = authorization?.Id,
            AuthorizationStatus = authorization?.Status,
            CreateTime = authorization?.CreateTime,
            ExpirationTime = authorization?.ExpirationTime
        };
    }

    public async Task<PayPalReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string payPalRequestId, CancellationToken ct = default)
    {
        var request = new ReauthorizeRequest { Amount = new MoneyDto { CurrencyCode = currencyCode, Value = FormatAmount(amount) } };
        var response = await SendJsonAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", request, payPalRequestId, ct);
        var result = await ReadOrThrowAsync<AuthorizationResponse>(response, ct);

        return new PayPalReauthorizationResult
        {
            AuthorizationId = result.Id,
            Status = result.Status,
            CreateTime = result.CreateTime,
            ExpirationTime = result.ExpirationTime
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        using var response = await SendJsonAsync<object?>(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, null, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowPayPalErrorAsync(response, ct);
        }
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currencyCode, string payPalRequestId, CancellationToken ct = default)
    {
        var request = new CaptureRequest
        {
            Amount = new MoneyDto { CurrencyCode = currencyCode, Value = FormatAmount(amount) },
            FinalCapture = true
        };
        var response = await SendJsonAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", request, payPalRequestId, ct);
        var result = await ReadOrThrowAsync<CaptureResponse>(response, ct);

        var gross = ParseAmount(result.SellerReceivableBreakdown?.GrossAmount ?? result.Amount) ?? amount;
        var fee = ParseAmount(result.SellerReceivableBreakdown?.PayPalFee) ?? 0m;
        var net = ParseAmount(result.SellerReceivableBreakdown?.NetAmount) ?? gross - fee;

        return new PayPalCaptureResult
        {
            CaptureId = result.Id,
            Status = result.Status,
            GrossAmount = gross,
            FeeAmount = fee,
            NetAmount = net,
            CurrencyCode = currencyCode,
            CaptureTime = result.CreateTime ?? DateTimeOffset.UtcNow
        };
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currencyCode, string payPalRequestId, CancellationToken ct = default)
    {
        var request = new RefundRequest { Amount = new MoneyDto { CurrencyCode = currencyCode, Value = FormatAmount(amount) } };
        var response = await SendJsonAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", request, payPalRequestId, ct);
        var result = await ReadOrThrowAsync<RefundResponse>(response, ct);

        return new PayPalRefundResult
        {
            RefundId = result.Id,
            Status = result.Status,
            Amount = ParseAmount(result.Amount) ?? amount,
            CurrencyCode = currencyCode,
            CreateTime = result.CreateTime ?? DateTimeOffset.UtcNow
        };
    }

    public async Task<PayPalVaultCardResult> SaveCardAsync(PayPalCardDetails card, CancellationToken ct = default)
    {
        var setupRequest = new SetupTokenRequest
        {
            PaymentSource = new SetupTokenPaymentSourceRequest
            {
                Card = new VaultCardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    Name = card.CardholderName,
                    Cvv = card.SecurityCode,
                    BillingAddress = BuildBillingAddress(card),
                    VerificationMethod = "SCA_WHEN_REQUIRED",
                    // Required by the schema even for a pure server-to-server card save with no
                    // buyer redirect; these are never actually visited unless PayPal returns
                    // PAYER_ACTION_REQUIRED, which this integration treats as an unsupported flow.
                    ExperienceContext = new ExperienceContextDto
                    {
                        ReturnUrl = "https://example.com/paypal/return",
                        CancelUrl = "https://example.com/paypal/cancel"
                    }
                }
            }
        };

        var setupResponse = await SendJsonAsync(HttpMethod.Post, "/v3/vault/setup-tokens", setupRequest, $"vault-setup-{Guid.NewGuid():N}", ct);
        var setupResult = await ReadOrThrowAsync<SetupTokenResponse>(setupResponse, ct);

        if (string.Equals(setupResult.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalActionRequiredException(
                "PayPal requires the shopper to complete an additional verification step to save this card, which this integration does not support.");
        }

        var tokenRequest = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenPaymentSourceRequest
            {
                Token = new TokenRefDto { Id = setupResult.Id, Type = "SETUP_TOKEN" }
            }
        };

        var tokenResponse = await SendJsonAsync(HttpMethod.Post, "/v3/vault/payment-tokens", tokenRequest, $"vault-token-{Guid.NewGuid():N}", ct);
        var tokenResult = await ReadOrThrowAsync<PaymentTokenResponse>(tokenResponse, ct);

        return new PayPalVaultCardResult
        {
            PaymentTokenId = tokenResult.Id,
            CustomerId = tokenResult.Customer?.Id ?? setupResult.Customer?.Id ?? string.Empty,
            CardBrand = tokenResult.PaymentSource?.Card?.Brand,
            LastDigits = tokenResult.PaymentSource?.Card?.LastDigits,
            Expiry = tokenResult.PaymentSource?.Card?.Expiry
        };
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct = default)
    {
        using var response = await SendJsonAsync<object?>(HttpMethod.Delete, $"/v3/vault/payment-tokens/{paymentTokenId}", null, null, ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            await ThrowPayPalErrorAsync(response, ct);
        }
    }

    public async Task<PayPalTransactionSearchResult> SearchTransactionsPageAsync(DateTimeOffset from, DateTimeOffset to, int page, int pageSize, CancellationToken ct = default)
    {
        var query = "start_date=" + Uri.EscapeDataString(FormatDate(from)) +
                    "&end_date=" + Uri.EscapeDataString(FormatDate(to)) +
                    "&page=" + page +
                    "&page_size=" + pageSize +
                    "&fields=all";

        var response = await SendGetAsync($"/v1/reporting/transactions?{query}", ct);
        var result = await ReadOrThrowAsync<TransactionSearchResponse>(response, ct);

        var transactions = (result.TransactionDetails ?? new List<TransactionDetailDto>())
            .Where(d => d.TransactionInfo is not null)
            .Select(d =>
            {
                var info = d.TransactionInfo!;
                return new PayPalTransactionRecord
                {
                    TransactionId = info.TransactionId,
                    PayPalReferenceId = info.PayPalReferenceId,
                    EventCode = info.TransactionEventCode ?? string.Empty,
                    Status = info.TransactionStatus ?? string.Empty,
                    Amount = ParseAmount(info.TransactionAmount) ?? 0m,
                    CurrencyCode = info.TransactionAmount?.CurrencyCode ?? string.Empty,
                    InvoiceId = info.InvoiceId,
                    InitiationDate = info.TransactionInitiationDate ?? default
                };
            })
            .ToList();

        return new PayPalTransactionSearchResult
        {
            Transactions = transactions,
            Page = result.Page == 0 ? page : result.Page,
            TotalPages = result.TotalPages
        };
    }

    private static BillingAddressDto BuildBillingAddress(PayPalCardDetails card) => new()
    {
        AddressLine1 = card.AddressLine1,
        AddressLine2 = card.AddressLine2,
        AdminArea2 = card.City,
        AdminArea1 = card.State,
        PostalCode = card.PostalCode,
        CountryCode = card.CountryCode
    };

    private static CardPaymentSourceRequest BuildCardPaymentSourceRequest(PayPalCardDetails card)
    {
        return new CardPaymentSourceRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = BuildBillingAddress(card),
            Attributes = new CardAttributesDto { Verification = new VerificationDto { Method = "SCA_WHEN_REQUIRED" } }
        };
    }

    private Uri BuildUri(string pathAndQuery) => new(_options.ResolveBaseUrl() + pathAndQuery);

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(MoneyDto? money) =>
        money is null ? null : decimal.Parse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _cachedAccessTokenExpiry)
        {
            return _cachedAccessToken;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _cachedAccessTokenExpiry)
            {
                return _cachedAccessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowPayPalErrorAsync(response, ct);
            }

            var token = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(JsonOptions, ct);
            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                throw new PayPalOperationException("PayPal did not return an access token.");
            }

            _cachedAccessToken = token.AccessToken;
            _cachedAccessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, token.ExpiresIn - 60));
            return _cachedAccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendJsonAsync<TRequest>(HttpMethod method, string path, TRequest? body, string? payPalRequestId, CancellationToken ct)
    {
        return await SendWithRetryAsync(async () =>
        {
            var token = await GetAccessTokenAsync(ct);
            using var request = new HttpRequestMessage(method, BuildUri(path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(payPalRequestId))
            {
                request.Headers.Add("PayPal-Request-Id", payPalRequestId);
            }
            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                request.Content.Headers.ContentType!.CharSet = null; // PayPal expects a bare "application/json" content type
            }
            return await _httpClient.SendAsync(request, ct);
        }, ct);
    }

    private async Task<HttpResponseMessage> SendGetAsync(string pathAndQuery, CancellationToken ct)
    {
        return await SendWithRetryAsync(async () =>
        {
            var token = await GetAccessTokenAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(pathAndQuery));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await _httpClient.SendAsync(request, ct);
        }, ct);
    }

    /// <summary>Retries transient network failures and 5xx responses a couple of times with
    /// backoff. PayPal-Request-Id on POSTs makes a retried call safe to repeat.</summary>
    private static async Task<HttpResponseMessage> SendWithRetryAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await send();
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < maxAttempts)
            {
                response.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
                continue;
            }

            return response;
        }

        // Unreachable: loop always returns or retries until the final attempt returns.
        throw new PayPalOperationException("PayPal request failed after retries.");
    }

    private async Task<T> ReadOrThrowAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
                if (result is null)
                {
                    throw new PayPalOperationException("PayPal returned a success status with an empty response body.");
                }
                return result;
            }

            await ThrowPayPalErrorAsync(response, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    private static async Task ThrowPayPalErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? name = null;
        string? message = null;
        string? debugId = null;

        var raw = await response.Content.ReadAsStringAsync(ct);

        try
        {
            var error = JsonSerializer.Deserialize<PayPalErrorResponse>(raw, JsonOptions);
            name = error?.Name;
            debugId = error?.DebugId;
            message = error?.Details is { Count: > 0 }
                ? string.Join("; ", error.Details.Select(d => d.Description ?? d.Issue))
                : error?.Message;
        }
        catch (JsonException)
        {
            // Body wasn't valid PayPal error JSON - fall through with what we have.
        }

        var suffix = message is not null ? $": {message}" : ".";
        throw new PayPalOperationException($"PayPal request failed with HTTP {(int)response.StatusCode} {response.StatusCode}{suffix}", name, debugId);
    }

    // ----- Wire DTOs (PayPal's snake_case REST contract, confirmed against developer.paypal.com/docs) -----

    private class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private class MoneyDto
    {
        [JsonPropertyName("currency_code")] public string CurrencyCode { get; set; } = default!;
        [JsonPropertyName("value")] public string Value { get; set; } = default!;
    }

    private class CreateOrderRequest
    {
        [JsonPropertyName("intent")] public string Intent { get; set; } = "AUTHORIZE";
        [JsonPropertyName("purchase_units")] public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
        [JsonPropertyName("payment_source")] public PaymentSourceRequest PaymentSource { get; set; } = default!;
    }

    private class PurchaseUnitRequest
    {
        [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
        [JsonPropertyName("amount")] public MoneyDto Amount { get; set; } = default!;
    }

    private class PaymentSourceRequest
    {
        [JsonPropertyName("card")] public CardPaymentSourceRequest Card { get; set; } = default!;
    }

    private class CardPaymentSourceRequest
    {
        [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
        [JsonPropertyName("number")] public string? Number { get; set; }
        [JsonPropertyName("expiry")] public string? Expiry { get; set; }
        [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("billing_address")] public BillingAddressDto? BillingAddress { get; set; }
        [JsonPropertyName("attributes")] public CardAttributesDto? Attributes { get; set; }
    }

    private class BillingAddressDto
    {
        [JsonPropertyName("address_line_1")] public string AddressLine1 { get; set; } = default!;
        [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
        [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }
        [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }
        [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
        [JsonPropertyName("country_code")] public string CountryCode { get; set; } = default!;
    }

    private class CardAttributesDto
    {
        [JsonPropertyName("verification")] public VerificationDto? Verification { get; set; }
    }

    private class VerificationDto
    {
        [JsonPropertyName("method")] public string Method { get; set; } = "SCA_WHEN_REQUIRED";
    }

    private class OrderResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = default!;
        [JsonPropertyName("status")] public string Status { get; set; } = default!;
        [JsonPropertyName("purchase_units")] public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
    }

    private class PurchaseUnitResponse
    {
        [JsonPropertyName("payments")] public PaymentsCollectionDto? Payments { get; set; }
    }

    private class PaymentsCollectionDto
    {
        [JsonPropertyName("authorizations")] public List<AuthorizationResponse>? Authorizations { get; set; }
    }

    private class AuthorizationResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = default!;
        [JsonPropertyName("status")] public string Status { get; set; } = default!;
        [JsonPropertyName("create_time")] public DateTimeOffset? CreateTime { get; set; }
        [JsonPropertyName("expiration_time")] public DateTimeOffset? ExpirationTime { get; set; }
    }

    private class ReauthorizeRequest
    {
        [JsonPropertyName("amount")] public MoneyDto Amount { get; set; } = default!;
    }

    private class CaptureRequest
    {
        [JsonPropertyName("amount")] public MoneyDto Amount { get; set; } = default!;
        [JsonPropertyName("final_capture")] public bool FinalCapture { get; set; }
    }

    private class CaptureResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = default!;
        [JsonPropertyName("status")] public string Status { get; set; } = default!;
        [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
        [JsonPropertyName("seller_receivable_breakdown")] public SellerReceivableBreakdownDto? SellerReceivableBreakdown { get; set; }
        [JsonPropertyName("create_time")] public DateTimeOffset? CreateTime { get; set; }
    }

    private class SellerReceivableBreakdownDto
    {
        [JsonPropertyName("gross_amount")] public MoneyDto? GrossAmount { get; set; }
        [JsonPropertyName("paypal_fee")] public MoneyDto? PayPalFee { get; set; }
        [JsonPropertyName("net_amount")] public MoneyDto? NetAmount { get; set; }
    }

    private class RefundRequest
    {
        [JsonPropertyName("amount")] public MoneyDto Amount { get; set; } = default!;
    }

    private class RefundResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = default!;
        [JsonPropertyName("status")] public string Status { get; set; } = default!;
        [JsonPropertyName("amount")] public MoneyDto? Amount { get; set; }
        [JsonPropertyName("create_time")] public DateTimeOffset? CreateTime { get; set; }
    }

    private class SetupTokenRequest
    {
        [JsonPropertyName("payment_source")] public SetupTokenPaymentSourceRequest PaymentSource { get; set; } = default!;
    }

    private class SetupTokenPaymentSourceRequest
    {
        [JsonPropertyName("card")] public VaultCardRequest Card { get; set; } = default!;
    }

    private class VaultCardRequest
    {
        [JsonPropertyName("number")] public string Number { get; set; } = default!;
        [JsonPropertyName("expiry")] public string Expiry { get; set; } = default!;
        [JsonPropertyName("name")] public string Name { get; set; } = default!;
        [JsonPropertyName("cvv")] public string? Cvv { get; set; }
        [JsonPropertyName("billing_address")] public BillingAddressDto? BillingAddress { get; set; }
        [JsonPropertyName("verification_method")] public string? VerificationMethod { get; set; }
        [JsonPropertyName("experience_context")] public ExperienceContextDto? ExperienceContext { get; set; }
    }

    private class ExperienceContextDto
    {
        [JsonPropertyName("return_url")] public string ReturnUrl { get; set; } = default!;
        [JsonPropertyName("cancel_url")] public string CancelUrl { get; set; } = default!;
    }

    private class SetupTokenResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = default!;
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("customer")] public CustomerDto? Customer { get; set; }
    }

    private class CustomerDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = default!;
    }

    private class PaymentTokenRequest
    {
        [JsonPropertyName("payment_source")] public PaymentTokenPaymentSourceRequest PaymentSource { get; set; } = default!;
    }

    private class PaymentTokenPaymentSourceRequest
    {
        [JsonPropertyName("token")] public TokenRefDto Token { get; set; } = default!;
    }

    private class TokenRefDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = default!;
        [JsonPropertyName("type")] public string Type { get; set; } = "SETUP_TOKEN";
    }

    private class PaymentTokenResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = default!;
        [JsonPropertyName("customer")] public CustomerDto? Customer { get; set; }
        [JsonPropertyName("payment_source")] public PaymentTokenSourceResponse? PaymentSource { get; set; }
    }

    private class PaymentTokenSourceResponse
    {
        [JsonPropertyName("card")] public VaultCardResponse? Card { get; set; }
    }

    private class VaultCardResponse
    {
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
        [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    }

    private class TransactionSearchResponse
    {
        [JsonPropertyName("transaction_details")] public List<TransactionDetailDto>? TransactionDetails { get; set; }
        [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
        [JsonPropertyName("page")] public int Page { get; set; }
    }

    private class TransactionDetailDto
    {
        [JsonPropertyName("transaction_info")] public TransactionInfoDto? TransactionInfo { get; set; }
    }

    private class TransactionInfoDto
    {
        [JsonPropertyName("transaction_id")] public string TransactionId { get; set; } = default!;
        [JsonPropertyName("paypal_reference_id")] public string? PayPalReferenceId { get; set; }
        [JsonPropertyName("transaction_event_code")] public string? TransactionEventCode { get; set; }
        [JsonPropertyName("transaction_status")] public string? TransactionStatus { get; set; }
        [JsonPropertyName("transaction_amount")] public MoneyDto? TransactionAmount { get; set; }
        [JsonPropertyName("transaction_initiation_date")] public DateTimeOffset? TransactionInitiationDate { get; set; }
        [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    }

    private class PayPalErrorResponse
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
        [JsonPropertyName("details")] public List<PayPalErrorDetailDto>? Details { get; set; }
    }

    private class PayPalErrorDetailDto
    {
        [JsonPropertyName("issue")] public string? Issue { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }
}
