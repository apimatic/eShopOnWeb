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
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalGateway : IPayPalGateway
{
    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string TokenPath = "/v1/oauth2/token";
    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxTransactionSearchRange = TimeSpan.FromDays(31);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PayPalOrderResult> CreateAuthorizedOrderAsync(CreateAuthorizedOrderCommand command, CancellationToken cancellationToken = default)
    {
        var body = new CreateOrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequestDto>
            {
                new()
                {
                    ReferenceId = "default",
                    InvoiceId = command.InvoiceId,
                    CustomId = command.CustomId,
                    Amount = ToMoney(command.Currency, command.Amount)
                }
            },
            PaymentSource = new PaymentSourceRequestDto
            {
                Card = command.VaultId is not null
                    ? new CardRequestDto
                    {
                        VaultId = command.VaultId,
                        StoredCredential = new StoredCredentialDto
                        {
                            PaymentInitiator = "CUSTOMER",
                            PaymentType = "UNSCHEDULED",
                            Usage = "SUBSEQUENT"
                        }
                    }
                    : MapCard(command.Card)
            }
        };

        var order = await SendAsync<CreateOrderRequestDto, OrderDto>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            body,
            command.IdempotencyKey,
            cancellationToken);

        EnsureNoPayerActionRequired(order);
        var result = MapOrder(order);

        if (result.Authorizations.Count == 0 && !string.IsNullOrEmpty(result.Id))
        {
            result = await GetOrderAsync(result.Id, cancellationToken);
        }

        EnsureAuthorizationPresent(result);
        return result;
    }

    public async Task<PayPalOrderResult> GetOrderAsync(string payPalOrderId, CancellationToken cancellationToken = default)
    {
        var order = await SendAsync<object, OrderDto>(
            HttpMethod.Get,
            $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}",
            null,
            requestId: null,
            cancellationToken);

        EnsureNoPayerActionRequired(order);
        return MapOrder(order);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var auth = await SendAsync<object, AuthorizationDto>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            null,
            requestId: null,
            cancellationToken);

        return MapAuthorization(auth);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequestDto { Amount = ToMoney(currency, amount) };
        var auth = await SendAsync<ReauthorizeRequestDto, AuthorizationDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            body,
            requestId,
            cancellationToken);

        return MapAuthorization(auth);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new CaptureRequestDto
        {
            Amount = ToMoney(currency, amount),
            FinalCapture = true
        };

        var capture = await SendAsync<CaptureRequestDto, CaptureDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            body,
            requestId,
            cancellationToken);

        return MapCapture(capture);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object, AuthorizationDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            body: null,
            requestId,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new RefundRequestDto { Amount = ToMoney(currency, amount) };
        var refund = await SendAsync<RefundRequestDto, RefundDto>(
            HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body,
            requestId,
            cancellationToken);

        return MapRefund(refund);
    }

    public async Task<PayPalVaultedCardResult> VaultCardAsync(CardPaymentSource card, string merchantCustomerId, string? payPalCustomerId, string requestId, CancellationToken cancellationToken = default)
    {
        var customer = new VaultCustomerDto { MerchantCustomerId = merchantCustomerId };
        if (!string.IsNullOrEmpty(payPalCustomerId))
        {
            customer.Id = payPalCustomerId;
        }

        var body = new CreatePaymentTokenRequestDto
        {
            Customer = customer,
            PaymentSource = new PaymentSourceRequestDto { Card = MapCard(card) }
        };

        var token = await SendAsync<CreatePaymentTokenRequestDto, PaymentTokenResponseDto>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            body,
            requestId,
            cancellationToken);

        if (string.IsNullOrEmpty(token.Id))
        {
            throw new PaymentException("PayPal did not return a vault payment token id.", 502);
        }

        var cardResponse = token.PaymentSource?.Card;
        return new PayPalVaultedCardResult(
            token.Id,
            token.Customer?.Id,
            cardResponse?.LastDigits,
            cardResponse?.Brand,
            cardResponse?.Expiry,
            cardResponse?.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object, object>(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}",
            null,
            requestId: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> SearchAllTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("`to` must be greater than or equal to `from`.");
        }

        var results = new List<PayPalReportedTransaction>();
        var windowStart = from;
        while (windowStart <= to)
        {
            var windowEnd = windowStart + MaxTransactionSearchRange;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await SearchWindowAsync(windowStart, windowEnd, results, cancellationToken);

            if (windowEnd >= to)
            {
                break;
            }

            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task SearchWindowAsync(DateTimeOffset from, DateTimeOffset to, List<PayPalReportedTransaction> results, CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var query = new Dictionary<string, string?>
            {
                ["start_date"] = FormatPayPalDate(from),
                ["end_date"] = FormatPayPalDate(to),
                ["page"] = page.ToString(CultureInfo.InvariantCulture),
                ["page_size"] = "500",
                ["fields"] = "transaction_info",
                ["balance_affecting_records_only"] = "N"
            };

            var path = "/v1/reporting/transactions" + ToQueryString(query);
            TransactionSearchResponseDto response;
            try
            {
                response = await SendAsync<object, TransactionSearchResponseDto>(
                    HttpMethod.Get,
                    path,
                    null,
                    requestId: null,
                    cancellationToken);
            }
            catch (PaymentException ex) when (ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("PayPal reporting has no data for {From} to {To}: {Message}", from, to, ex.Message);
                return;
            }

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null || string.IsNullOrEmpty(info.TransactionId))
                    {
                        continue;
                    }

                    results.Add(new PayPalReportedTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.PaypalReferenceIdType,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        info.InvoiceId,
                        info.CustomField,
                        MapMoney(info.TransactionAmount),
                        MapMoney(info.FeeAmount),
                        ParseDate(info.TransactionInitiationDate)));
                }
            }

            totalPages = response.TotalPages.GetValueOrDefault(1);
            page++;
        } while (page <= totalPages);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowNoContent = false)
        where TResponse : class
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body is not null && method != HttpMethod.Get)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method.Method, RedactPath(path));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || (allowNoContent && response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(payload)))
        {
            return Activator.CreateInstance<TResponse>();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Activator.CreateInstance<TResponse>();
        }

        var parsed = JsonSerializer.Deserialize<TResponse>(payload, JsonOptions);
        return parsed ?? Activator.CreateInstance<TResponse>();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                throw new PaymentException("PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret.", 500);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(TokenPath));
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            _logger.LogInformation("PayPal POST {Path}", TokenPath);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode, payload);
            }

            var token = JsonSerializer.Deserialize<OAuthTokenResponse>(payload, JsonOptions);
            if (token?.AccessToken is null)
            {
                throw new PaymentException("PayPal token response did not include an access_token.", 502);
            }

            _accessToken = token.AccessToken;
            var lifetime = TimeSpan.FromSeconds(Math.Max(token.ExpiresIn, 60));
            _tokenExpiresAt = DateTimeOffset.UtcNow + lifetime - TokenRefreshSkew;
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? SandboxBaseUrl
            : _settings.BaseUrl.TrimEnd('/');
        return new Uri(baseUrl + path);
    }

    private static CardRequestDto MapCard(CardPaymentSource? card)
    {
        if (card is null)
        {
            throw new PaymentException("Card details or a saved payment method are required.");
        }

        return new CardRequestDto
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = card.BillingAddress is null
                ? null
                : new AddressDto
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
        };
    }

    private static PayPalOrderResult MapOrder(OrderDto order)
    {
        var authorizations = order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationDto>())
            .Select(MapAuthorization)
            .ToList() ?? new List<PayPalAuthorizationResult>();

        var captures = order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Captures ?? Enumerable.Empty<CaptureDto>())
            .Select(MapCapture)
            .ToList() ?? new List<PayPalCaptureResult>();

        var links = order.Links?
            .Where(l => l.Rel is not null && l.Href is not null)
            .Select(l => new PayPalLink(l.Rel!, l.Href!))
            .ToList() ?? new List<PayPalLink>();

        return new PayPalOrderResult(order.Id ?? string.Empty, order.Status ?? string.Empty, links, authorizations, captures);
    }

    private static PayPalAuthorizationResult MapAuthorization(AuthorizationDto auth)
    {
        return new PayPalAuthorizationResult(
            auth.Id ?? string.Empty,
            auth.Status ?? string.Empty,
            MapMoney(auth.Amount),
            ParseDate(auth.CreateTime),
            ParseDate(auth.ExpirationTime),
            auth.StatusDetails?.Reason);
    }

    private static PayPalCaptureResult MapCapture(CaptureDto capture)
    {
        var breakdown = capture.SellerReceivableBreakdown;
        return new PayPalCaptureResult(
            capture.Id ?? string.Empty,
            capture.Status ?? string.Empty,
            MapMoney(breakdown?.GrossAmount) ?? MapMoney(capture.Amount),
            MapMoney(breakdown?.PaypalFee),
            MapMoney(breakdown?.NetAmount),
            ParseDate(capture.CreateTime));
    }

    private static PayPalRefundResult MapRefund(RefundDto refund)
    {
        return new PayPalRefundResult(
            refund.Id ?? string.Empty,
            refund.Status ?? string.Empty,
            MapMoney(refund.Amount),
            ParseDate(refund.CreateTime));
    }

    private static void EnsureNoPayerActionRequired(OrderDto order)
    {
        var needsAction = string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || (order.Links?.Any(l => l.Rel is not null && l.Rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase)) ?? false);

        if (needsAction)
        {
            throw new PayPalPayerActionRequiredException(
                "PayPal required a shopper to complete an approval challenge in a browser (for example 3-D Secure). This integration does not collect a browser round-trip, so the payment was not completed.");
        }
    }

    private static void EnsureAuthorizationPresent(PayPalOrderResult order)
    {
        if (order.Authorizations.Count == 0)
        {
            throw new PaymentException(
                $"PayPal did not authorize the payment. Order status: {order.Status}.",
                402);
        }

        var authorization = order.Authorizations[0];
        if (string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"PayPal denied the authorization ({authorization.StatusReason ?? authorization.Status}).",
                402);
        }
    }

    private static MoneyDto ToMoney(string currency, decimal amount)
    {
        return new MoneyDto
        {
            CurrencyCode = currency,
            Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
        };
    }

    private static PayPalMoney? MapMoney(MoneyDto? money)
    {
        if (money?.Value is null || money.CurrencyCode is null)
        {
            return null;
        }

        if (!decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return new PayPalMoney(money.CurrencyCode, value);
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatPayPalDate(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string ToQueryString(Dictionary<string, string?> values)
    {
        var parts = values
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");
        return "?" + string.Join("&", parts);
    }

    private static string RedactPath(string path)
    {
        return path;
    }

    private PaymentException CreateApiException(HttpStatusCode statusCode, string payload)
    {
        PayPalErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorResponse>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            // Body is not the documented error schema; fall through to a generic message.
        }

        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var description = error?.Details?.FirstOrDefault()?.Description;
        var message = error?.Message ?? "PayPal request failed.";
        var name = error?.Name;

        if (IsPayerActionIssue(issue, name, description, payload))
        {
            return new PayPalPayerActionRequiredException(
                "PayPal required a shopper to complete an approval challenge in a browser (for example 3-D Secure). This integration does not collect a browser round-trip, so the payment was not completed.");
        }

        var mappedStatus = statusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            (HttpStatusCode)422 => 409,
            _ => 502
        };

        var detail = string.Join("; ", new[] { name, issue, description }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var debug = string.IsNullOrEmpty(error?.DebugId) ? string.Empty : $" PayPal debug_id={error.DebugId}.";
        return new PaymentException($"{message}{(string.IsNullOrEmpty(detail) ? string.Empty : " " + detail)}.{debug}".Trim(), mappedStatus);
    }

    private static bool IsPayerActionIssue(string? issue, string? name, string? description, string payload)
    {
        var haystack = $"{issue} {name} {description}";
        if (haystack.Contains("PAYER_ACTION", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("3DS", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("THREE_DS", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("CONTINGENCY", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return payload.Contains("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase);
    }
}
