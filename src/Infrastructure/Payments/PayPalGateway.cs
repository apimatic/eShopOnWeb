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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex CardNumberJson = new("\"number\"\\s*:\\s*\"[0-9]+\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SecurityCodeJson = new("\"security_code\"\\s*:\\s*\"[0-9]+\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalAccessTokenService _tokens;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(
        IHttpClientFactory httpClientFactory,
        PayPalAccessTokenService tokens,
        ILogger<PayPalGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokens = tokens;
        _logger = logger;
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        string invoiceId,
        string customId,
        decimal amount,
        string currency,
        PayPalCardDetails card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var source = new PayPalPaymentSource
        {
            Card = ToCardRequest(card)
        };
        return CreateAuthorizedOrderAsync(invoiceId, customId, amount, currency, source, requestId, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardPaymentAsync(
        string invoiceId,
        string customId,
        decimal amount,
        string currency,
        string vaultId,
        string requestId,
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
        return CreateAuthorizedOrderAsync(invoiceId, customId, amount, currency, source, requestId, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            null,
            requestId: null,
            cancellationToken);
        return ToAuthorizationDetails(dto);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalReauthorizeRequest { Amount = Money(amount, currency) };
        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId,
            cancellationToken);
        return ToAuthorizationDetails(dto);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCaptureRequest
        {
            Amount = Money(amount, currency),
            InvoiceId = invoiceId,
            FinalCapture = true
        };
        var dto = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            cancellationToken);
        return ToCaptureResult(dto, currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            null,
            requestId,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = amount.HasValue
            ? new PayPalRefundRequest { Amount = Money(amount.Value, currency) }
            : new PayPalRefundRequest();
        var dto = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken);
        var refunded = dto.Amount?.Value != null ? MoneyFormatter.Parse(dto.Amount.Value) : amount ?? 0m;
        return new PayPalRefundResult(
            dto.Id ?? throw Missing("refund id"),
            dto.Status ?? "COMPLETED",
            refunded,
            dto.Amount?.CurrencyCode ?? currency);
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        string customerId,
        string merchantCustomerId,
        PayPalCardDetails card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalVaultRequest
        {
            Customer = new PayPalVaultCustomer
            {
                Id = customerId,
                MerchantCustomerId = merchantCustomerId
            },
            PaymentSource = new PayPalVaultPaymentSource
            {
                Card = ToCardRequest(card)
            }
        };

        var dto = await SendAsync<PayPalVaultResponse>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            body,
            requestId,
            cancellationToken);

        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new PaymentGatewayException("PayPal vault response did not include a payment token id.");
        }

        var lastDigits = dto.PaymentSource?.Card?.LastDigits;
        if (string.IsNullOrEmpty(lastDigits))
        {
            var digits = new string(card.Number.Where(char.IsDigit).ToArray());
            lastDigits = digits.Length <= 4 ? digits : digits[^4..];
        }

        return new PayPalVaultedCard(
            dto.Id,
            dto.Customer?.Id ?? customerId,
            lastDigits,
            dto.PaymentSource?.Card?.Brand,
            dto.PaymentSource?.Card?.Expiry,
            dto.PaymentSource?.Card?.Name ?? card.Name);
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{vaultId}",
            null,
            requestId: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        foreach (var window in SplitInto31DayWindows(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                var query = QueryString(window.From, window.To, page);
                PayPalSearchResponse? response;
                try
                {
                    response = await SendAsync<PayPalSearchResponse>(
                        HttpMethod.Get,
                        $"/v1/reporting/transactions{query}",
                        null,
                        requestId: null,
                        cancellationToken);
                }
                catch (PaymentGatewayException ex) when (IsReportingDataUnavailable(ex))
                {
                    _logger.LogWarning(
                        "PayPal reporting has no data for {From} to {To}: {Message}",
                        window.From, window.To, ex.Message);
                    break;
                }

                if (response.TransactionDetails != null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info?.TransactionId == null)
                        {
                            continue;
                        }

                        DateTimeOffset? initiated = null;
                        if (!string.IsNullOrEmpty(info.TransactionInitiationDate)
                            && DateTimeOffset.TryParse(info.TransactionInitiationDate, CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind, out var parsed))
                        {
                            initiated = parsed;
                        }

                        results.Add(new PayPalReportedTransaction(
                            info.TransactionId,
                            info.PaypalReferenceId,
                            info.InvoiceId,
                            info.CustomField,
                            info.TransactionEventCode,
                            info.TransactionStatus,
                            info.TransactionAmount?.Value == null ? null : MoneyFormatter.Parse(info.TransactionAmount.Value),
                            info.TransactionAmount?.CurrencyCode,
                            initiated,
                            info.FeeAmount?.Value == null ? null : MoneyFormatter.Parse(info.FeeAmount.Value)));
                    }
                }

                totalPages = response.TotalPages ?? page;
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<PayPalAuthorizationResult> CreateAuthorizedOrderAsync(
        string invoiceId,
        string customId,
        decimal amount,
        string currency,
        PayPalPaymentSource paymentSource,
        string requestId,
        CancellationToken cancellationToken)
    {
        var request = new PayPalOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits =
            {
                new PayPalPurchaseUnitRequest
                {
                    InvoiceId = invoiceId,
                    CustomId = customId,
                    Amount = Money(amount, currency)
                }
            },
            PaymentSource = paymentSource
        };

        var order = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            request,
            requestId,
            cancellationToken);

        EnsureNoPayerAction(order);

        var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization == null && string.Equals(order.Status, "APPROVED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(order.Id))
        {
            order = await SendAsync<PayPalOrderResponse>(
                HttpMethod.Post,
                $"/v2/checkout/orders/{order.Id}/authorize",
                new { },
                requestId + "-authorize",
                cancellationToken);
            EnsureNoPayerAction(order);
            authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        }

        if (authorization?.Id == null)
        {
            throw new PaymentGatewayException(
                $"PayPal did not return an authorization for invoice {invoiceId}. Order status was {order.Status}.");
        }

        DateTimeOffset? expires = null;
        if (!string.IsNullOrEmpty(authorization.ExpirationTime)
            && DateTimeOffset.TryParse(authorization.ExpirationTime, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsedExpiry))
        {
            expires = parsedExpiry;
        }

        var authorizedAmount = authorization.Amount?.Value == null
            ? amount
            : MoneyFormatter.Parse(authorization.Amount.Value);

        return new PayPalAuthorizationResult(
            order.Id ?? throw Missing("PayPal order id"),
            order.Status ?? "COMPLETED",
            authorization.Id,
            authorization.Status ?? "CREATED",
            authorizedAmount,
            authorization.Amount?.CurrencyCode ?? currency,
            expires);
    }

    private static void EnsureNoPayerAction(PayPalOrderResponse order)
    {
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || order.Links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper to complete a browser challenge (for example 3-D Secure) before the payment can proceed. " +
                "This integration does not implement an approval round-trip.");
        }
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowNoContent = false)
    {
        var client = _httpClientFactory.CreateClient("PayPal");
        var token = await _tokens.GetAccessTokenAsync(cancellationToken);

        using var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(requestId))
        {
            message.Headers.TryAddWithoutValidation("PayPal-Request-Id", Truncate(requestId, 108));
        }

        if (method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            message.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method.Method, Redact(path));

        using var response = await client.SendAsync(message, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(responseBody))
        {
            if (allowNoContent && response.IsSuccessStatusCode)
            {
                return default!;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw ToGatewayException(response.StatusCode, responseBody);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal {Method} {Path} failed with {Status}: {Body}",
                method.Method, path, (int)response.StatusCode, Redact(responseBody));
            throw ToGatewayException(response.StatusCode, responseBody);
        }

        if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(responseBody))
        {
            return default!;
        }

        var parsed = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        if (parsed == null)
        {
            throw new PaymentGatewayException($"PayPal returned an empty body for {path}.");
        }

        return parsed;
    }

    private static PaymentGatewayException ToGatewayException(HttpStatusCode status, string body)
    {
        PayPalErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorResponse>(body, JsonOptions);
        }
        catch (JsonException)
        {
            // body is not the documented error schema
        }

        var details = error?.Details == null
            ? null
            : string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}".Trim(' ', ':')));
        var message = error?.Message ?? $"PayPal request failed with {(int)status}.";
        if (!string.IsNullOrEmpty(details))
        {
            message = $"{message} {details}";
        }

        return new PaymentGatewayException(message, error?.Name, error?.DebugId);
    }

    private static bool IsReportingDataUnavailable(PaymentGatewayException ex)
    {
        var blob = $"{ex.PayPalErrorName} {ex.Message}".ToUpperInvariant();
        return blob.Contains("NOT AVAILABLE")
               || blob.Contains("DATA_NOT_AVAILABLE");
    }

    private static PayPalAuthorizationDetails ToAuthorizationDetails(PayPalAuthorizationDto dto)
    {
        DateTimeOffset? expires = null;
        if (!string.IsNullOrEmpty(dto.ExpirationTime)
            && DateTimeOffset.TryParse(dto.ExpirationTime, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
        {
            expires = parsed;
        }

        return new PayPalAuthorizationDetails(
            dto.Id ?? throw Missing("authorization id"),
            dto.Status ?? "CREATED",
            dto.Amount?.Value == null ? 0m : MoneyFormatter.Parse(dto.Amount.Value),
            dto.Amount?.CurrencyCode ?? "",
            expires);
    }

    private static PayPalCaptureResult ToCaptureResult(PayPalCaptureDto dto, string fallbackCurrency)
    {
        var breakdown = dto.SellerReceivableBreakdown;
        return new PayPalCaptureResult(
            dto.Id ?? throw Missing("capture id"),
            dto.Status ?? "COMPLETED",
            dto.Amount?.Value == null ? MoneyFormatter.Parse(breakdown?.GrossAmount?.Value) : MoneyFormatter.Parse(dto.Amount.Value),
            breakdown?.PaypalFee?.Value == null ? null : MoneyFormatter.Parse(breakdown.PaypalFee.Value),
            breakdown?.NetAmount?.Value == null ? null : MoneyFormatter.Parse(breakdown.NetAmount.Value),
            dto.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? fallbackCurrency);
    }

    private static PayPalCardRequest ToCardRequest(PayPalCardDetails card)
    {
        return new PayPalCardRequest
        {
            Name = card.Name,
            Number = new string(card.Number.Where(char.IsDigit).ToArray()),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = card.BillingAddress == null
                ? null
                : new PayPalAddressDto
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
        };
    }

    private static PayPalMoneyDto Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = MoneyFormatter.ToPayPalValue(amount, currency)
    };

    private static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> SplitInto31DayWindows(DateTimeOffset from, DateTimeOffset to)
    {
        var cursor = from;
        while (cursor < to)
        {
            var windowEnd = cursor.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            yield return (cursor, windowEnd);
            cursor = windowEnd;
        }

        if (cursor == from)
        {
            yield return (from, to);
        }
    }

    private static string QueryString(DateTimeOffset from, DateTimeOffset to, int page)
    {
        var start = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var end = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        return $"?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&page={page}&page_size=500&fields=all&balance_affecting_records_only=N";
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string Redact(string value)
    {
        var redacted = CardNumberJson.Replace(value, "\"number\":\"***\"");
        return SecurityCodeJson.Replace(redacted, "\"security_code\":\"***\"");
    }

    private static Exception Missing(string name) =>
        new PaymentGatewayException($"PayPal response was missing {name}.");
}
