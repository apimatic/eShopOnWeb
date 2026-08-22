using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "HUF", "TWD"
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? CachedAccessToken;
    private static DateTimeOffset TokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalSettings> options, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<PayPalOrderResult> CreateAuthorizeOrderAsync(
        decimal amount,
        string currency,
        string customId,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["custom_id"] = customId,
                    ["invoice_id"] = invoiceId,
                    ["amount"] = Money(amount, currency)
                }
            }
        };

        var response = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            payload,
            requestId,
            cancellationToken);

        return MapOrder(response);
    }

    public async Task<PayPalOrderResult> AuthorizeOrderAsync(
        string payPalOrderId,
        CardPayment? card,
        string? vaultId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = BuildPaymentSource(card, vaultId);
        var payload = new Dictionary<string, object?>
        {
            ["payment_source"] = paymentSource
        };

        var response = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            $"/v2/checkout/orders/{payPalOrderId}/authorize",
            payload,
            requestId,
            cancellationToken);

        return MapOrder(response);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalAuthorizationResponse>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            null,
            null,
            cancellationToken);

        return MapAuthorization(response);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["amount"] = Money(amount, currency)
        };

        var response = await SendAsync<PayPalAuthorizationResponse>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            payload,
            requestId,
            cancellationToken);

        return MapAuthorization(response);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<PayPalAuthorizationResponse>(
                HttpMethod.Post,
                $"/v2/payments/authorizations/{authorizationId}/void",
                new Dictionary<string, object?>(),
                requestId,
                cancellationToken);
        }
        catch (CheckoutException ex) when (ex.StatusCode == 422 || ex.StatusCode == 400)
        {
            // Already voided is treated as success for idempotent cancel.
            if (!ex.Message.Contains("VOIDED", StringComparison.OrdinalIgnoreCase)
                && !ex.Message.Contains("already voided", StringComparison.OrdinalIgnoreCase)
                && !ex.Message.Contains("previously voided", StringComparison.OrdinalIgnoreCase))
            {
                throw;
            }
        }
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["amount"] = Money(amount, currency),
            ["invoice_id"] = invoiceId,
            ["final_capture"] = true
        };

        var response = await SendAsync<PayPalCaptureResponse>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            payload,
            requestId,
            cancellationToken);

        var result = MapCapture(response);
        if (result.PayPalFee is null || result.NetAmount is null)
        {
            var fresh = await GetCaptureAsync(result.Id, cancellationToken);
            if (fresh.PayPalFee is not null || fresh.NetAmount is not null)
            {
                return fresh;
            }
        }

        return result;
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalCaptureResponse>(
            HttpMethod.Get,
            $"/v2/payments/captures/{captureId}",
            null,
            null,
            cancellationToken);

        return MapCapture(response);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        object payload;
        if (amount.HasValue)
        {
            payload = new Dictionary<string, object?>
            {
                ["amount"] = Money(amount.Value, currency)
            };
        }
        else
        {
            payload = new Dictionary<string, object?>();
        }

        var response = await SendAsync<PayPalRefundResponse>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            payload,
            requestId,
            cancellationToken);

        return new PayPalRefundResult
        {
            Id = response.Id ?? string.Empty,
            Status = response.Status ?? string.Empty,
            Amount = ParseMoney(response.Amount?.Value) ?? amount ?? 0m,
            Currency = response.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCardResult> SaveCardAsync(
        CardPayment card,
        string? payPalCustomerId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var setupPayload = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["card"] = BuildCardObject(card, includeExperienceContext: true)
            }
        };

        if (!string.IsNullOrWhiteSpace(payPalCustomerId))
        {
            setupPayload["customer"] = new Dictionary<string, object?> { ["id"] = payPalCustomerId };
        }

        var setup = await SendAsync<PayPalSetupTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            setupPayload,
            requestId,
            cancellationToken);

        EnsureNoPayerAction(setup.Status, setup.Links);

        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException(502,
                $"PayPal did not approve the card for vaulting (status '{setup.Status}').");
        }

        var tokenPayload = new Dictionary<string, object?>
        {
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, object?>
                {
                    ["id"] = setup.Id,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };

        var token = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            tokenPayload,
            $"{requestId}-token",
            cancellationToken);

        var lastDigits = token.PaymentSource?.Card?.LastDigits
            ?? setup.PaymentSource?.Card?.LastDigits
            ?? string.Empty;

        return new PayPalVaultedCardResult
        {
            VaultId = token.Id ?? string.Empty,
            CustomerId = token.Customer?.Id ?? setup.Customer?.Id,
            LastDigits = lastDigits,
            Brand = token.PaymentSource?.Card?.Brand ?? setup.PaymentSource?.Card?.Brand,
            Expiry = token.PaymentSource?.Card?.Expiry ?? setup.PaymentSource?.Card?.Expiry,
            CardholderName = token.PaymentSource?.Card?.Name ?? setup.PaymentSource?.Card?.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<object>(
                HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{vaultId}",
                null,
                null,
                cancellationToken);
        }
        catch (CheckoutException ex) when (ex.StatusCode == 404)
        {
            // Already deleted at PayPal; local removal can proceed.
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new CheckoutException(400, "Reconciliation 'to' must be on or after 'from'.");
        }

        var results = new List<PayPalTransactionRecord>();
        foreach (var (chunkFrom, chunkTo) in SplitIntoReportingWindows(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                var start = FormatReportingTimestamp(chunkFrom);
                var end = FormatReportingTimestamp(chunkTo);
                var path =
                    $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=all&page_size=500&page={page}";

                var response = await SendAsync<PayPalReportingResponse>(
                    HttpMethod.Get,
                    path,
                    null,
                    null,
                    cancellationToken);

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null || string.IsNullOrEmpty(info.TransactionId))
                        {
                            continue;
                        }

                        results.Add(new PayPalTransactionRecord
                        {
                            TransactionId = info.TransactionId,
                            PaypalReferenceId = info.PaypalReferenceId,
                            InvoiceId = info.InvoiceId,
                            CustomField = info.CustomField,
                            Status = info.TransactionStatus,
                            EventCode = info.TransactionEventCode,
                            InitiationDate = ParseTimestamp(info.TransactionInitiationDate),
                            Amount = ParseMoney(info.TransactionAmount?.Value),
                            Currency = info.TransactionAmount?.CurrencyCode,
                            FeeAmount = ParseMoney(info.FeeAmount?.Value)
                        });
                    }
                }

                totalPages = response.TotalPages is > 0 ? response.TotalPages.Value : 1;
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> SplitIntoReportingWindows(
        DateTimeOffset from,
        DateTimeOffset to)
    {
        // Transaction Search API supports a maximum range of 31 days per request.
        var cursor = from;
        do
        {
            var chunkEnd = cursor.AddDays(31);
            if (chunkEnd > to)
            {
                chunkEnd = to;
            }

            yield return (cursor, chunkEnd);
            if (chunkEnd >= to)
            {
                yield break;
            }

            cursor = chunkEnd;
        } while (true);
    }

    private Dictionary<string, object?> BuildPaymentSource(CardPayment? card, string? vaultId)
    {
        if (!string.IsNullOrWhiteSpace(vaultId))
        {
            return new Dictionary<string, object?>
            {
                ["card"] = new Dictionary<string, object?>
                {
                    ["vault_id"] = vaultId
                }
            };
        }

        if (card is null)
        {
            throw new CheckoutException(400, "A card or a saved payment method is required to pay.");
        }

        return new Dictionary<string, object?>
        {
            ["card"] = BuildCardObject(card, includeExperienceContext: false)
        };
    }

    private static Dictionary<string, object?> BuildCardObject(CardPayment card, bool includeExperienceContext)
    {
        var cardObject = new Dictionary<string, object?>
        {
            ["number"] = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
            ["expiry"] = NormalizeExpiry(card.Expiry),
            ["security_code"] = card.SecurityCode,
            ["name"] = card.Name
        };

        var billing = card.BillingAddress;
        if (billing is null || string.IsNullOrWhiteSpace(billing.CountryCode))
        {
            billing = new CardBillingAddress(
                billing?.AddressLine1 ?? "123 Main St.",
                billing?.AddressLine2,
                billing?.AdminArea2 ?? "Anytown",
                billing?.AdminArea1 ?? "CA",
                billing?.PostalCode ?? "12345",
                billing?.CountryCode ?? "US");
        }
        if (billing is not null)
        {
            cardObject["billing_address"] = new Dictionary<string, object?>
            {
                ["address_line_1"] = billing.AddressLine1,
                ["address_line_2"] = billing.AddressLine2,
                ["admin_area_2"] = billing.AdminArea2,
                ["admin_area_1"] = billing.AdminArea1,
                ["postal_code"] = billing.PostalCode,
                ["country_code"] = string.IsNullOrWhiteSpace(billing.CountryCode) ? "US" : billing.CountryCode
            };
        }

        if (includeExperienceContext)
        {
            cardObject["experience_context"] = new Dictionary<string, object?>
            {
                ["brand_name"] = "eShopOnWeb",
                ["locale"] = "en-US",
                ["return_url"] = "https://example.com/returnUrl",
                ["cancel_url"] = "https://example.com/cancelUrl"
            };
        }

        return cardObject;
    }

    private static string NormalizeExpiry(string expiry)
    {
        expiry = expiry.Trim();
        if (Regex.IsMatch(expiry, @"^\d{4}-\d{2}$"))
        {
            return expiry;
        }

        var parts = expiry.Split('/', '-', ' ');
        if (parts.Length == 2 && parts[0].Length is 1 or 2 && parts[1].Length is 2 or 4)
        {
            var month = parts[0].PadLeft(2, '0');
            var year = parts[1].Length == 2 ? $"20{parts[1]}" : parts[1];
            return $"{year}-{month}";
        }

        throw new CheckoutException(400, "Card expiry must be in YYYY-MM format.");
    }

    private PayPalOrderResult MapOrder(PayPalOrderResponse response)
    {
        EnsureNoPayerAction(response.Status, response.Links);
        var authorization = response.PurchaseUnits?
            .Find(u => u.Payments?.Authorizations is { Count: > 0 })?
            .Payments?.Authorizations?[0];

        return new PayPalOrderResult
        {
            Id = response.Id ?? string.Empty,
            Status = response.Status ?? string.Empty,
            Authorization = authorization is null ? null : MapAuthorization(authorization),
            RequiresPayerAction = IsPayerAction(response.Status, response.Links)
        };
    }

    private static PayPalAuthorizationResult MapAuthorization(PayPalAuthorizationResponse response)
    {
        return new PayPalAuthorizationResult
        {
            Id = response.Id ?? string.Empty,
            Status = response.Status ?? string.Empty,
            ExpirationTime = ParseTimestamp(response.ExpirationTime),
            CreateTime = ParseTimestamp(response.CreateTime),
            Amount = ParseMoney(response.Amount?.Value),
            Currency = response.Amount?.CurrencyCode
        };
    }

    private static PayPalCaptureResult MapCapture(PayPalCaptureResponse response)
    {
        var breakdown = response.SellerReceivableBreakdown;
        return new PayPalCaptureResult
        {
            Id = response.Id ?? string.Empty,
            Status = response.Status ?? string.Empty,
            Amount = ParseMoney(breakdown?.GrossAmount?.Value) ?? ParseMoney(response.Amount?.Value) ?? 0m,
            Currency = breakdown?.GrossAmount?.CurrencyCode ?? response.Amount?.CurrencyCode ?? string.Empty,
            PayPalFee = ParseMoney(breakdown?.PaypalFee?.Value),
            NetAmount = ParseMoney(breakdown?.NetAmount?.Value)
        };
    }

    private static void EnsureNoPayerAction(string? status, List<PayPalLink>? links)
    {
        if (!IsPayerAction(status, links))
        {
            return;
        }

        throw new CheckoutException(409,
            "PayPal required a shopper approval step in the browser, which this integration does not support.");
    }

    private static bool IsPayerAction(string? status, List<PayPalLink>? links)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // "approve" is a standard Checkout HATEOAS link and does not mean a 3DS challenge.
        return links is not null && links.Exists(l =>
            string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, CombineUrl(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method.Method, Redact(path));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(responseBody))
        {
            if (response.IsSuccessStatusCode)
            {
                return Activator.CreateInstance<T>();
            }

            throw new CheckoutException((int)response.StatusCode,
                $"PayPal request failed with status {(int)response.StatusCode} and an empty body.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw MapError(response.StatusCode, responseBody);
        }

        if (typeof(T) == typeof(object))
        {
            return (T)(object)new object();
        }

        var parsed = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        if (parsed is null)
        {
            throw new CheckoutException(502, "PayPal returned an empty or unreadable response.");
        }

        return parsed;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (CachedAccessToken is not null && DateTimeOffset.UtcNow < TokenExpiresAt)
        {
            return CachedAccessToken;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (CachedAccessToken is not null && DateTimeOffset.UtcNow < TokenExpiresAt)
            {
                return CachedAccessToken;
            }

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new CheckoutException(500, "PayPal credentials are not configured.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, CombineUrl("/v1/oauth2/token"));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            _logger.LogInformation("PayPal POST /v1/oauth2/token");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw MapError(response.StatusCode, body);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(body, JsonOptions);
            if (token?.AccessToken is null)
            {
                throw new CheckoutException(502, "PayPal did not return an access token.");
            }

            CachedAccessToken = token.AccessToken;
            var lifetime = token.ExpiresIn > 60 ? token.ExpiresIn - 60 : Math.Max(token.ExpiresIn, 1);
            TokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime);
            return CachedAccessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private CheckoutException MapError(HttpStatusCode statusCode, string body)
    {
        var redacted = Redact(body);
        _logger.LogWarning("PayPal error {Status}: {Body}", (int)statusCode, redacted);

        try
        {
            var error = JsonSerializer.Deserialize<PayPalErrorResponse>(body, JsonOptions);
            var detail = error?.Details is { Count: > 0 }
                ? string.Join("; ", error.Details.ConvertAll(d => $"{d.Issue}: {d.Description}".Trim()))
                : null;
            var message = string.Join(" ", new[]
            {
                error?.Name,
                error?.Message,
                detail,
                string.IsNullOrEmpty(error?.DebugId) ? null : $"debug_id={error.DebugId}"
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            if (!string.IsNullOrWhiteSpace(message))
            {
                return new CheckoutException((int)statusCode, Redact(message));
            }
        }
        catch (JsonException)
        {
            // Fall through to generic message.
        }

        return new CheckoutException((int)statusCode, $"PayPal request failed with status {(int)statusCode}.");
    }

    private string CombineUrl(string path)
    {
        var baseUrl = _settings.ResolveBaseUrl();
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return $"{baseUrl}{path}";
    }

    private static Dictionary<string, string> Money(decimal amount, string currency) =>
        new()
        {
            ["currency_code"] = currency,
            ["value"] = FormatMoney(amount, currency)
        };

    internal static string FormatMoney(decimal amount, string currency)
    {
        if (ZeroDecimalCurrencies.Contains(currency))
        {
            return decimal.Truncate(amount).ToString("0", CultureInfo.InvariantCulture);
        }

        return amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static decimal? ParseMoney(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatReportingTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Redact(string value) =>
        Regex.Replace(value, @"\b[0-9]{13,19}\b", "[REDACTED]");
}
