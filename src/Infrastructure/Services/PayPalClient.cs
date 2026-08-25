using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class PayPalClient : IPayPalClient
{
    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;

    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private static readonly JsonSerializerOptions _serializeOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions _deserializeOpts = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public PayPalClient(IOptions<PayPalSettings> settings, ILogger<PayPalClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _http = new HttpClient();
    }

    private string BaseUrl => _settings.ResolvedBaseUrl;

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken != null && DateTimeOffset.UtcNow < _tokenExpiry)
            return _cachedToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken != null && DateTimeOffset.UtcNow < _tokenExpiry)
                return _cachedToken;

            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var response = await _http.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new PayPalException("TOKEN_ERROR", $"Failed to get PayPal access token: {content}");

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(content, _deserializeOpts)!;
            _cachedToken = token.AccessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 300);
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path,
        object? body = null, string? idempotencyKey = null, bool preferRepresentation = false,
        CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);

        var request = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (idempotencyKey != null)
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);

        if (preferRepresentation)
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        string? json = null;
        if (body != null)
        {
            json = JsonSerializer.Serialize(body, body.GetType(), _serializeOpts);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogDebug("PayPal {Method} {Path}", method, path);
        return await _http.SendAsync(request, ct);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path,
        string rawJson, string? idempotencyKey = null, bool preferRepresentation = false,
        CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey != null)
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        if (preferRepresentation)
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        request.Content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        _logger.LogDebug("PayPal {Method} {Path}", method, path);
        return await _http.SendAsync(request, ct);
    }

    private async Task<T> ParseResponseAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("PayPal error {Status}: {Content}", (int)response.StatusCode, content);
            PayPalErrorResponse? error = null;
            try { error = JsonSerializer.Deserialize<PayPalErrorResponse>(content, _deserializeOpts); }
            catch (JsonException) { }

            var msg = error?.Message ?? $"PayPal returned {(int)response.StatusCode}";
            if (error?.Details?.Count > 0)
                msg += $" | {string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}"))}";

            throw new PayPalException(error?.Name ?? "PAYPAL_ERROR", msg, error?.DebugId);
        }

        return JsonSerializer.Deserialize<T>(content, _deserializeOpts)!;
    }

    public async Task<PayPalOrderResult> CreateOrderWithCardAsync(decimal amount, string currency,
        CardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        var cardNode = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["security_code"] = card.SecurityCode,
            ["name"] = card.Name
        };
        if (card.BillingAddress != null)
        {
            cardNode["billing_address"] = new JsonObject
            {
                ["address_line_1"] = card.BillingAddress.Street,
                ["admin_area_2"] = card.BillingAddress.City,
                ["admin_area_1"] = card.BillingAddress.State,
                ["postal_code"] = card.BillingAddress.ZipCode,
                ["country_code"] = card.BillingAddress.CountryCode
            };
        }

        var bodyNode = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject { ["amount"] = new JsonObject { ["currency_code"] = currency, ["value"] = amount.ToString("F2") } }
            },
            ["payment_source"] = new JsonObject { ["card"] = cardNode }
        };

        var json = bodyNode.ToJsonString();
        var response = await SendRawAsync(HttpMethod.Post, "/v2/checkout/orders", json,
            idempotencyKey, preferRepresentation: true, ct);
        var result = await ParseResponseAsync<PayPalOrderResponse>(response, ct);
        return MapToOrderResult(result);
    }

    public async Task<PayPalOrderResult> CreateOrderWithVaultAsync(decimal amount, string currency,
        string vaultId, string idempotencyKey, CancellationToken ct = default)
    {
        var bodyNode = new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject { ["amount"] = new JsonObject { ["currency_code"] = currency, ["value"] = amount.ToString("F2") } }
            },
            ["payment_source"] = new JsonObject
            {
                ["card"] = new JsonObject
                {
                    ["vault_id"] = vaultId
                }
            }
        };

        var json = bodyNode.ToJsonString();
        var response = await SendRawAsync(HttpMethod.Post, "/v2/checkout/orders", json,
            idempotencyKey, preferRepresentation: true, ct);
        var result = await ParseResponseAsync<PayPalOrderResponse>(response, ct);
        return MapToOrderResult(result);
    }

    private PayPalOrderResult MapToOrderResult(PayPalOrderResponse result)
    {
        if (result.Status == "PAYER_ACTION_REQUIRED")
            throw new PayPalException("PAYER_ACTION_REQUIRED",
                "3DS verification required — payment cannot complete without browser interaction. " +
                "Use a card that does not trigger 3DS in the sandbox, or contact PayPal support.");

        var auth = result.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorizationInfo>())
            .FirstOrDefault();

        if (auth == null)
            throw new PayPalException("NO_AUTHORIZATION",
                $"PayPal returned order status '{result.Status}' but no authorization was created.");

        return new PayPalOrderResult
        {
            PayPalOrderId = result.Id!,
            AuthorizationId = auth.Id!,
            AuthorizationStatus = auth.Status ?? "CREATED",
            AuthorizationExpiry = auth.ExpirationTime ?? DateTimeOffset.UtcNow.AddDays(29),
            AuthorizationCreatedAt = auth.CreateTime ?? DateTimeOffset.UtcNow
        };
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId,
        string idempotencyKey, CancellationToken ct = default)
    {
        var body = new { };
        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body, idempotencyKey, preferRepresentation: true, ct);
        var result = await ParseResponseAsync<PayPalCaptureResponse>(response, ct);

        var bd = result.SellerReceivableBreakdown;
        return new PayPalCaptureResult
        {
            CaptureId = result.Id!,
            Status = result.Status ?? "COMPLETED",
            CapturedAmount = ParseDecimal(bd?.GrossAmount?.Value),
            PayPalFee = ParseDecimal(bd?.PaypalFee?.Value),
            NetAmount = ParseDecimal(bd?.NetAmount?.Value)
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", null, ct: ct);

        if ((int)response.StatusCode is not (200 or 204))
        {
            var content = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("PayPal void failed {Status}: {Content}", (int)response.StatusCode, content);
            PayPalErrorResponse? error = null;
            try { error = JsonSerializer.Deserialize<PayPalErrorResponse>(content, _deserializeOpts); }
            catch (JsonException) { }
            throw new PayPalException(error?.Name ?? "VOID_ERROR",
                error?.Message ?? $"Void failed with {(int)response.StatusCode}: {content}");
        }
    }

    public async Task<PayPalReauthorizeResult> ReauthorizeAsync(string authorizationId,
        decimal amount, string currency, CancellationToken ct = default)
    {
        var body = new { Amount = new { CurrencyCode = currency, Value = amount.ToString("F2") } };
        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body, preferRepresentation: true, ct: ct);
        var result = await ParseResponseAsync<PayPalReauthorizeResponse>(response, ct);

        return new PayPalReauthorizeResult
        {
            NewAuthorizationId = result.Id!,
            Status = result.Status ?? "CREATED",
            ExpirationTime = result.ExpirationTime ?? DateTimeOffset.UtcNow.AddDays(29)
        };
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount,
        string currency, string idempotencyKey, CancellationToken ct = default)
    {
        object body = amount.HasValue
            ? new { Amount = new { CurrencyCode = currency, Value = amount.Value.ToString("F2") } }
            : new { };

        var response = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body, idempotencyKey, preferRepresentation: true, ct);
        var result = await ParseResponseAsync<PayPalRefundResponse>(response, ct);

        return new PayPalRefundResult
        {
            RefundId = result.Id!,
            Status = result.Status ?? "COMPLETED",
            Amount = ParseDecimal(result.Amount?.Value)
        };
    }

    public async Task<PayPalSetupTokenResult> CreateSetupTokenAsync(CardDetails card,
        string? existingCustomerId, string idempotencyKey, CancellationToken ct = default)
    {
        // Use JsonNode to avoid anonymous-type-as-object serialization issues
        var cardNode = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["name"] = card.Name,
            ["experience_context"] = new JsonObject
            {
                ["return_url"] = "https://example.com/vault/return",
                ["cancel_url"] = "https://example.com/vault/cancel"
            }
        };
        if (card.BillingAddress != null)
        {
            cardNode["billing_address"] = new JsonObject
            {
                ["address_line_1"] = card.BillingAddress.Street,
                ["admin_area_2"] = card.BillingAddress.City,
                ["admin_area_1"] = card.BillingAddress.State,
                ["postal_code"] = card.BillingAddress.ZipCode,
                ["country_code"] = card.BillingAddress.CountryCode
            };
        }

        var bodyNode = new JsonObject
        {
            ["payment_source"] = new JsonObject { ["card"] = cardNode }
        };
        if (!string.IsNullOrEmpty(existingCustomerId))
            bodyNode["customer"] = new JsonObject { ["id"] = existingCustomerId };

        var json = bodyNode.ToJsonString();
        var token = await GetAccessTokenAsync(ct);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v3/vault/setup-tokens")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);

        _logger.LogDebug("PayPal POST /v3/vault/setup-tokens");
        var response = await _http.SendAsync(request, ct);
        var result = await ParseResponseAsync<PayPalSetupTokenResponse>(response, ct);

        if (result.Status == "PAYER_ACTION_REQUIRED")
            throw new PayPalException("PAYER_ACTION_REQUIRED",
                "3DS verification required — cannot vault card without browser interaction.");

        return new PayPalSetupTokenResult
        {
            SetupTokenId = result.Id!,
            CustomerId = result.Customer?.Id ?? "",
            LastFour = result.PaymentSource?.Card?.LastDigits ?? "",
            Brand = result.PaymentSource?.Card?.Brand ?? "",
            Expiry = result.PaymentSource?.Card?.Expiry ?? card.Expiry
        };
    }

    public async Task<PayPalVaultTokenResult> CreatePaymentTokenAsync(string setupTokenId,
        string idempotencyKey, CancellationToken ct = default)
    {
        var body = new
        {
            PaymentSource = new
            {
                Token = new { Id = setupTokenId, Type = "SETUP_TOKEN" }
            }
        };

        var response = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            body, idempotencyKey, ct: ct);
        var result = await ParseResponseAsync<PayPalVaultTokenResponse>(response, ct);

        return new PayPalVaultTokenResult
        {
            VaultId = result.Id!,
            CustomerId = result.Customer?.Id ?? "",
            LastFour = result.PaymentSource?.Card?.LastDigits ?? "",
            Brand = result.PaymentSource?.Card?.Brand ?? "",
            Expiry = result.PaymentSource?.Card?.Expiry ?? ""
        };
    }

    public async Task<List<PayPalVaultTokenResult>> ListPaymentTokensAsync(string customerId,
        CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"/v3/vault/payment-tokens?customer_id={Uri.EscapeDataString(customerId)}", ct: ct);
        var result = await ParseResponseAsync<PayPalListVaultTokensResponse>(response, ct);

        return (result.PaymentTokens ?? new List<PayPalVaultTokenResponse>())
            .Select(t => new PayPalVaultTokenResult
            {
                VaultId = t.Id!,
                CustomerId = customerId,
                LastFour = t.PaymentSource?.Card?.LastDigits ?? "",
                Brand = t.PaymentSource?.Card?.Brand ?? "",
                Expiry = t.PaymentSource?.Card?.Expiry ?? ""
            })
            .ToList();
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{tokenId}", ct: ct);

        if ((int)response.StatusCode is not (200 or 204))
        {
            var content = await response.Content.ReadAsStringAsync(ct);
            throw new PayPalException("DELETE_TOKEN_ERROR",
                $"Failed to delete vault token {tokenId}: {content}");
        }
    }

    public async Task<List<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
    {
        var all = new List<PayPalTransactionRecord>();
        var page = 1;
        int totalPages;

        do
        {
            var startStr = startDate.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endStr = endDate.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var path = $"/v1/reporting/transactions" +
                $"?start_date={Uri.EscapeDataString(startStr)}" +
                $"&end_date={Uri.EscapeDataString(endStr)}" +
                $"&fields=all&page_size=500&page={page}";

            var response = await SendAsync(HttpMethod.Get, path, ct: ct);
            var result = await ParseResponseAsync<PayPalTransactionSearchResponse>(response, ct);

            totalPages = result.TotalPages ?? 1;

            foreach (var detail in result.TransactionDetails ?? new List<PayPalTransactionDetail>())
            {
                var info = detail.TransactionInfo;
                if (info == null) continue;

                all.Add(new PayPalTransactionRecord
                {
                    TransactionId = info.TransactionId ?? "",
                    EventCode = info.TransactionEventCode ?? "",
                    Amount = ParseDecimal(info.TransactionAmount?.Value),
                    Currency = info.TransactionAmount?.CurrencyCode ?? "",
                    Status = info.TransactionStatus ?? "",
                    FeeAmount = ParseDecimal(info.FeeAmount?.Value),
                    InitiationDate = info.TransactionInitiationDate,
                    PayerEmail = detail.PayerInfo?.EmailAddress ?? ""
                });
            }

            page++;
        } while (page <= totalPages);

        return all;
    }

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0m;
        return decimal.TryParse(value,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var d) ? d : 0m;
    }
}
