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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private const string TokenCacheKey = "paypal:access_token";
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly PayPalSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(
        HttpClient http,
        IOptions<PayPalSettings> settings,
        IMemoryCache cache,
        ILogger<PayPalGateway> logger)
    {
        _http = http;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
        _http.BaseAddress ??= new Uri(_settings.ResolveBaseUrl().TrimEnd('/') + "/");
    }

    public string Currency =>
        string.IsNullOrWhiteSpace(_settings.Currency) ? "USD" : _settings.Currency.Trim().ToUpperInvariant();

    public Task<PaymentHold> AuthorizeCardPaymentAsync(
        int orderId,
        decimal amount,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var source = new PayPalPaymentSource
        {
            Card = ToCardRequest(card)
        };
        return AuthorizeAsync(orderId, amount, source, idempotencyKey, cancellationToken);
    }

    public Task<PaymentHold> AuthorizeVaultedCardPaymentAsync(
        int orderId,
        decimal amount,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var source = new PayPalPaymentSource
        {
            Card = new PayPalCardRequest
            {
                VaultId = vaultId,
                StoredCredential = new PayPalStoredCredential
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "ONE_TIME",
                    Usage = "SUBSEQUENT"
                }
            }
        };
        return AuthorizeAsync(orderId, amount, source, idempotencyKey, cancellationToken);
    }

    public async Task<PaymentAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var resource = await SendAsync<PayPalAuthorizationResource>(
            HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        return ToAuthorizationDetails(resource);
    }

    public async Task<PaymentAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var resource = await SendAsync<PayPalAuthorizationResource>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            new PayPalReauthorizeRequest
            {
                Amount = Money(amount)
            },
            idempotencyKey,
            cancellationToken);

        return ToAuthorizationDetails(resource);
    }

    public async Task<PaymentCapture> CaptureAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var resource = await SendAsync<PayPalCaptureResource>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            new PayPalCaptureRequest { FinalCapture = true },
            idempotencyKey,
            cancellationToken);

        if (resource == null || string.IsNullOrEmpty(resource.Id))
        {
            throw new CheckoutException(502, "PayPal capture did not return a capture id.");
        }

        var breakdown = resource.SellerReceivableBreakdown;
        var captured = PayPalMoney.Parse(breakdown?.GrossAmount?.Value ?? resource.Amount?.Value);
        return new PaymentCapture
        {
            CaptureId = resource.Id,
            Status = resource.Status ?? string.Empty,
            CapturedAmount = captured,
            PaypalFee = breakdown?.PaypalFee?.Value != null ? PayPalMoney.Parse(breakdown.PaypalFee.Value) : null,
            NetAmount = breakdown?.NetAmount?.Value != null ? PayPalMoney.Parse(breakdown.NetAmount.Value) : null
        };
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorizationResource>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void",
            body: new { },
            requestId: idempotencyKey,
            cancellationToken);
    }

    public async Task<PaymentRefund> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var resource = await SendAsync<PayPalRefundResource>(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            new PayPalRefundRequest
            {
                Amount = Money(amount, currency)
            },
            idempotencyKey,
            cancellationToken);

        if (resource == null || string.IsNullOrEmpty(resource.Id))
        {
            throw new CheckoutException(502, "PayPal refund did not return a refund id.");
        }

        return new PaymentRefund
        {
            RefundId = resource.Id,
            Status = resource.Status ?? string.Empty,
            Amount = PayPalMoney.Parse(resource.Amount?.Value)
        };
    }

    public async Task<VaultedCard> VaultCardAsync(
        string customerId,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            new PayPalPaymentTokenRequest
            {
                Customer = new PayPalVaultCustomer { Id = customerId },
                PaymentSource = new PayPalPaymentSource { Card = ToCardRequest(card) }
            },
            idempotencyKey,
            cancellationToken);

        if (response == null || string.IsNullOrEmpty(response.Id))
        {
            throw new CheckoutException(502, "PayPal vault did not return a payment token id.");
        }

        var vaulted = response.PaymentSource?.Card;
        var lastDigits = vaulted?.LastDigits;
        if (string.IsNullOrEmpty(lastDigits) && card.Number.Length >= 4)
        {
            lastDigits = card.Number[^4..];
        }

        return new VaultedCard
        {
            VaultId = response.Id,
            LastDigits = lastDigits ?? string.Empty,
            Brand = vaulted?.Brand ?? InferBrand(card.Number),
            Expiry = vaulted?.Expiry ?? card.Expiry,
            Name = vaulted?.Name ?? card.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(
            HttpMethod.Delete,
            $"v3/vault/payment-tokens/{vaultId}",
            body: null,
            requestId: null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from.ToUniversalTime();
        var end = to.ToUniversalTime();

        while (windowStart <= end)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > end)
            {
                windowEnd = end;
            }

            await AddWindowAsync(results, windowStart, windowEnd, cancellationToken);
            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task AddWindowAsync(
        List<PayPalReportedTransaction> results,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var start = FormatReportingDate(from);
            var end = FormatReportingDate(to);
            var path =
                $"v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&page_size=500&page={page}&fields=all";

            var response = await SendAsync<PayPalSearchResponse>(
                HttpMethod.Get, path, body: null, requestId: null, cancellationToken);

            if (response?.TransactionDetails != null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info == null || string.IsNullOrEmpty(info.TransactionId))
                    {
                        continue;
                    }

                    results.Add(new PayPalReportedTransaction
                    {
                        TransactionId = info.TransactionId,
                        ReferenceId = info.PaypalReferenceId,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        Currency = info.TransactionAmount?.CurrencyCode,
                        Amount = info.TransactionAmount?.Value != null
                            ? PayPalMoney.Parse(info.TransactionAmount.Value)
                            : null,
                        FeeAmount = info.FeeAmount?.Value != null ? PayPalMoney.Parse(info.FeeAmount.Value) : null,
                        InitiationDate = info.TransactionInitiationDate
                    });
                }
            }

            totalPages = response?.TotalPages > 0 ? response.TotalPages : 1;
            page++;
        } while (page <= totalPages);
    }

    private async Task<PaymentHold> AuthorizeAsync(
        int orderId,
        decimal amount,
        PayPalPaymentSource paymentSource,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var create = new PayPalOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits =
            {
                new PayPalPurchaseUnitRequest
                {
                    ReferenceId = orderId.ToString(CultureInfo.InvariantCulture),
                    CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                    InvoiceId = $"ESH{orderId}-{Guid.NewGuid():N}",
                    Description = $"eShopOnWeb order {orderId}",
                    Amount = Money(amount)
                }
            },
            PaymentSource = paymentSource
        };

        var order = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            "v2/checkout/orders",
            create,
            $"{idempotencyKey}-create",
            cancellationToken);

        EnsureNoPayerAction(order);

        var hold = TryExtractHold(order);
        if (hold != null)
        {
            return hold;
        }

        if (string.IsNullOrEmpty(order?.Id))
        {
            throw new CheckoutException(502, "PayPal did not return an order id when authorizing payment.");
        }

        var authorized = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            $"v2/checkout/orders/{order.Id}/authorize",
            new { },
            $"{idempotencyKey}-authorize",
            cancellationToken);

        EnsureNoPayerAction(authorized);
        hold = TryExtractHold(authorized) ?? TryExtractHold(order);
        if (hold == null)
        {
            throw new CheckoutException(502, "PayPal authorized the order but did not return an authorization id.");
        }

        return hold;
    }

    private static void EnsureNoPayerAction(PayPalOrderResponse? order)
    {
        if (string.Equals(order?.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException(409,
                "PayPal required a shopper approval step in the browser, which this integration does not support.");
        }
    }

    private static PaymentHold? TryExtractHold(PayPalOrderResponse? order)
    {
        var authorization = order?.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorizationResource>())
            .FirstOrDefault(a => !string.IsNullOrEmpty(a.Id));

        if (authorization?.Id == null || order?.Id == null)
        {
            return null;
        }

        return new PaymentHold
        {
            PayPalOrderId = order.Id,
            AuthorizationId = authorization.Id,
            Status = authorization.Status ?? string.Empty,
            CreatedAt = authorization.CreateTime,
            ExpiresAt = authorization.ExpirationTime
        };
    }

    private static PaymentAuthorizationDetails ToAuthorizationDetails(PayPalAuthorizationResource? resource)
    {
        if (resource == null || string.IsNullOrEmpty(resource.Id))
        {
            throw new CheckoutException(502, "PayPal did not return authorization details.");
        }

        return new PaymentAuthorizationDetails
        {
            AuthorizationId = resource.Id,
            Status = resource.Status ?? string.Empty,
            CreatedAt = resource.CreateTime,
            ExpiresAt = resource.ExpirationTime
        };
    }

    private PayPalMoneyAmount Money(decimal amount, string? currency = null) =>
        new()
        {
            CurrencyCode = currency ?? Currency,
            Value = PayPalMoney.Format(amount, currency ?? Currency)
        };

    private static PayPalCardRequest ToCardRequest(CardPaymentDetails card)
    {
        return new PayPalCardRequest
        {
            Name = card.Name,
            Number = new string(card.Number.Where(char.IsDigit).ToArray()),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = card.BillingAddress == null
                ? new PayPalCardAddress
                {
                    AddressLine1 = "123 Main St.",
                    AdminArea2 = "San Jose",
                    AdminArea1 = "CA",
                    PostalCode = "95131",
                    CountryCode = "US"
                }
                : new PayPalCardAddress
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode)
                        ? "US"
                        : card.BillingAddress.CountryCode
                }
        };
    }

    private static string InferBrand(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        if (digits.StartsWith('4')) return "VISA";
        if (digits.StartsWith('5')) return "MASTERCARD";
        if (digits.StartsWith('3')) return "AMEX";
        return "UNKNOWN";
    }

    private static string FormatReportingDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method, SanitizePath(path));

        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(payload))
        {
            if (response.IsSuccessStatusCode)
            {
                return default;
            }

            throw ToCheckoutException(response.StatusCode, payload);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw ToCheckoutException(response.StatusCode, payload);
        }

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(TokenCacheKey, out cached) && !string.IsNullOrEmpty(cached))
            {
                return cached;
            }

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new CheckoutException(500, "PayPal credentials are not configured.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            _logger.LogInformation("PayPal POST v1/oauth2/token");
            using var response = await _http.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ToCheckoutException(response.StatusCode, payload);
            }

            var token = JsonSerializer.Deserialize<PayPalAccessTokenResponse>(payload, JsonOptions);
            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                throw new CheckoutException(502, "PayPal did not return an access token.");
            }

            var lifetime = TimeSpan.FromSeconds(Math.Max(token.ExpiresIn - 60, 30));
            _cache.Set(TokenCacheKey, token.AccessToken, lifetime);
            return token.AccessToken;
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private CheckoutException ToCheckoutException(HttpStatusCode statusCode, string payload)
    {
        PayPalErrorBody? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorBody>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            // Body is not the documented error model; keep the raw status.
        }

        var issues = error?.Details?
            .Where(d => !string.IsNullOrEmpty(d.Issue))
            .Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}")
            .ToList() ?? new List<string?>();

        var message = error?.Message ?? "PayPal request failed.";
        if (issues.Count > 0)
        {
            message = $"{message} {string.Join("; ", issues)}";
        }

        _logger.LogWarning(
            "PayPal error {Status} name={Name} debugId={DebugId} message={Message}",
            (int)statusCode,
            error?.Name,
            error?.DebugId,
            error?.Message);

        var mapped = statusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            (HttpStatusCode)422 => 422,
            _ => 502
        };

        return new CheckoutException(mapped, message);
    }

    private static string SanitizePath(string path)
    {
        var queryIndex = path.IndexOf('?');
        return queryIndex >= 0 ? path[..queryIndex] : path;
    }
}
