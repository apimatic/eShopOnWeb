using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Concrete PayPal gateway built directly against the PayPal OpenAPI specs (Checkout Orders v2, Payments v2,
/// Payment Method Tokens v3, Transaction Search v1). No third-party PayPal SDK is used.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private const int MaxReportWindowDays = 31; // Transaction Search: maximum supported range is 31 days.
    private const int ReportPageSize = 500;     // Transaction Search: page_size max is 500.

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPayPalTokenProvider _tokenProvider;
    private readonly PayPalOptions _options;

    public PayPalGateway(IHttpClientFactory httpClientFactory, IPayPalTokenProvider tokenProvider, IOptions<PayPalOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options.Value;
    }

    // ---------------- Checkout Orders v2: create + authorize ----------------

    public async Task<AuthorizeResult> CreateAndAuthorizeOrderAsync(AuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        PpCard card;
        if (request.VaultId is not null)
        {
            card = new PpCard { VaultId = request.VaultId };
        }
        else if (request.Card is not null)
        {
            var c = request.Card;
            card = new PpCard
            {
                Number = c.Number,
                Expiry = c.ToPayPalExpiry(),
                SecurityCode = c.SecurityCode,
                Name = c.CardholderName,
                BillingAddress = new PpBillingAddress
                {
                    AddressLine1 = c.BillingAddressLine1,
                    AddressLine2 = c.BillingAddressLine2,
                    AdminArea2 = c.BillingCity,
                    AdminArea1 = c.BillingState,
                    PostalCode = c.BillingPostalCode,
                    CountryCode = string.IsNullOrWhiteSpace(c.BillingCountryCode) ? "US" : c.BillingCountryCode
                }
            };
        }
        else
        {
            throw new ArgumentException("AuthorizeRequest must carry either a card or a vault id.", nameof(request));
        }

        var body = new PpCreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PpPurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = "default",
                    InvoiceId = request.InvoiceId,
                    CustomId = request.CustomId,
                    Amount = new PpMoney(request.CurrencyCode, PayPalMoney.Format(request.Amount, request.CurrencyCode))
                }
            },
            PaymentSource = new PpPaymentSource { Card = card }
        };

        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = request.RequestId,
            ["Prefer"] = "return=representation"
        };

        var order = await SendAsync<PpOrderResponse>(HttpMethod.Post, "/v2/checkout/orders", body, headers, cancellationToken);

        var instrumentDescription = DescribeInstrument(order?.PaymentSource?.Card);
        var status = order?.Status ?? "UNKNOWN";

        // A payer-approval challenge (e.g. 3DS redirect) is not supported by this API-only flow.
        var requiresPayerAction = string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || (order?.Links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false);
        if (requiresPayerAction)
        {
            return new AuthorizeResult(order?.Id ?? string.Empty, status, null, null, null, instrumentDescription, true);
        }

        var authorization = order?.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

        return new AuthorizeResult(
            order?.Id ?? string.Empty,
            status,
            authorization?.Id,
            authorization?.Status,
            ParseTimestamp(authorization?.ExpirationTime),
            instrumentDescription,
            false);
    }

    // ---------------- Payments v2 ----------------

    public async Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var auth = await SendAsync<PpAuthorizationResponse>(HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return new AuthorizationDetails(auth?.Id ?? authorizationId, auth?.Status ?? "UNKNOWN", ParseTimestamp(auth?.ExpirationTime));
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new PpReauthorizeRequest { Amount = new PpMoney(currencyCode, PayPalMoney.Format(amount, currencyCode)) };
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = requestId,
            ["Prefer"] = "return=representation"
        };
        var auth = await SendAsync<PpAuthorizationResponse>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, headers, cancellationToken);
        return new ReauthorizeResult(auth?.Id ?? authorizationId, auth?.Status ?? "UNKNOWN", ParseTimestamp(auth?.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal? amount, string currencyCode, string requestId, string? invoiceId, CancellationToken cancellationToken = default)
    {
        var body = new PpCaptureRequest
        {
            FinalCapture = true,
            InvoiceId = invoiceId,
            Amount = amount.HasValue ? new PpMoney(currencyCode, PayPalMoney.Format(amount.Value, currencyCode)) : null
        };
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = requestId,
            ["Prefer"] = "return=representation"
        };
        var capture = await SendAsync<PpCaptureResponse>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body, headers, cancellationToken);

        var breakdown = capture?.SellerReceivableBreakdown;
        var gross = PayPalMoney.ParseOrNull(breakdown?.GrossAmount?.Value)
            ?? PayPalMoney.ParseOrNull(capture?.Amount?.Value)
            ?? amount
            ?? 0m;
        var currency = breakdown?.GrossAmount?.CurrencyCode ?? capture?.Amount?.CurrencyCode ?? currencyCode;

        return new CaptureResult(
            capture?.Id ?? string.Empty,
            capture?.Status ?? "UNKNOWN",
            gross,
            PayPalMoney.ParseOrNull(breakdown?.PaypalFee?.Value),
            PayPalMoney.ParseOrNull(breakdown?.NetAmount?.Value),
            currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string> { ["PayPal-Request-Id"] = requestId };
        await SendNoContentAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, headers, cancellationToken);
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currencyCode, string requestId, string? invoiceId, string? customId, CancellationToken cancellationToken = default)
    {
        var body = new PpRefundRequest
        {
            InvoiceId = invoiceId,
            CustomId = customId,
            Amount = amount.HasValue ? new PpMoney(currencyCode, PayPalMoney.Format(amount.Value, currencyCode)) : null
        };
        var headers = new Dictionary<string, string>
        {
            ["PayPal-Request-Id"] = requestId,
            ["Prefer"] = "return=representation"
        };
        var refund = await SendAsync<PpRefundResponse>(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body, headers, cancellationToken);

        return new RefundResult(
            refund?.Id ?? string.Empty,
            refund?.Status ?? "UNKNOWN",
            PayPalMoney.ParseOrNull(refund?.Amount?.Value) ?? amount ?? 0m,
            refund?.Amount?.CurrencyCode ?? currencyCode);
    }

    // ---------------- Vault v3 ----------------

    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string? existingCustomerId, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new PpCreatePaymentTokenRequest
        {
            Customer = string.IsNullOrWhiteSpace(existingCustomerId) ? null : new PpCustomer { Id = existingCustomerId },
            PaymentSource = new PpVaultPaymentSource
            {
                Card = new PpCard
                {
                    Number = card.Number,
                    Expiry = card.ToPayPalExpiry(),
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = new PpBillingAddress
                    {
                        AddressLine1 = card.BillingAddressLine1,
                        AddressLine2 = card.BillingAddressLine2,
                        AdminArea2 = card.BillingCity,
                        AdminArea1 = card.BillingState,
                        PostalCode = card.BillingPostalCode,
                        CountryCode = string.IsNullOrWhiteSpace(card.BillingCountryCode) ? "US" : card.BillingCountryCode
                    }
                }
            }
        };
        var headers = new Dictionary<string, string> { ["PayPal-Request-Id"] = requestId };

        var token = await SendAsync<PpPaymentTokenResponse>(HttpMethod.Post, "/v3/vault/payment-tokens", body, headers, cancellationToken);

        var responseCard = token?.PaymentSource?.Card;
        return new VaultCardResult(
            token?.Id ?? throw new PayPalApiException(502, "VAULT_NO_ID", "PayPal vault response did not contain a token id.", null, Array.Empty<string>()),
            token.Customer?.Id ?? string.Empty,
            responseCard?.Brand ?? "UNKNOWN",
            responseCard?.LastDigits ?? "????",
            responseCard?.Expiry ?? card.ToPayPalExpiry(),
            responseCard?.Name ?? card.CardholderName);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendNoContentAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, cancellationToken);
    }

    // ---------------- Transaction Search v1 ----------------

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Walk the range in <=31-day windows (the API's maximum supported range), fully paging each window.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(MaxReportWindowDays);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            var totalPages = 1;
            do
            {
                var response = await FetchTransactionPageAsync(windowStart, windowEnd, page, cancellationToken);
                totalPages = Math.Max(1, response?.TotalPages ?? 1);

                foreach (var detail in response?.TransactionDetails ?? Enumerable.Empty<PpTransactionDetail>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }

                    // Dedupe across overlapping window boundaries.
                    var key = $"{info.TransactionId}|{info.TransactionEventCode}";
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    results.Add(new PayPalTransaction(
                        info.TransactionId,
                        info.TransactionEventCode,
                        ParseTimestamp(info.TransactionInitiationDate),
                        PayPalMoney.ParseOrNull(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        PayPalMoney.ParseOrNull(info.FeeAmount?.Value),
                        info.TransactionStatus,
                        info.InvoiceId,
                        info.CustomField,
                        info.PaypalReferenceId));
                }

                page++;
            }
            while (page <= totalPages);

            // Advance one second past the window end so the next window does not repeat the boundary instant.
            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task<PpSearchResponse?> FetchTransactionPageAsync(DateTimeOffset start, DateTimeOffset end, int page, CancellationToken cancellationToken)
    {
        var query = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(FormatReportDate(start))}" +
                    $"&end_date={Uri.EscapeDataString(FormatReportDate(end))}" +
                    $"&fields=transaction_info&balance_affecting_records_only=N" +
                    $"&page_size={ReportPageSize}&page={page}";
        return await SendAsync<PpSearchResponse>(HttpMethod.Get, query, null, null, cancellationToken);
    }

    // ---------------- HTTP plumbing ----------------

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, IDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, path, body, headers, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToApiException(response.StatusCode, content);
        }
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }
        return JsonSerializer.Deserialize<T>(content, PayPalHttpDefaults.JsonOptions);
    }

    private async Task SendNoContentAsync(HttpMethod method, string path, object? body, IDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, path, body, headers, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            throw ToApiException(response.StatusCode, content);
        }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body, IDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(PayPalHttpDefaults.ClientName);
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, body.GetType(), PayPalHttpDefaults.JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private static PayPalApiException ToApiException(HttpStatusCode statusCode, string content)
    {
        string? name = null;
        string? debugId = null;
        string message;
        var issues = new List<string>();

        try
        {
            var error = JsonSerializer.Deserialize<PpErrorResponse>(content, PayPalHttpDefaults.JsonOptions);
            name = error?.Name;
            debugId = error?.DebugId;
            message = error?.Message ?? $"PayPal returned HTTP {(int)statusCode}.";
            foreach (var detail in error?.Details ?? new List<PpErrorDetail>())
            {
                var issue = detail.Issue ?? detail.Description ?? "UNKNOWN_ISSUE";
                issues.Add(detail.Field is null ? issue : $"{issue} ({detail.Field})");
            }
        }
        catch (JsonException)
        {
            message = $"PayPal returned HTTP {(int)statusCode}.";
        }

        var full = issues.Count > 0
            ? $"{name}: {message} [{string.Join("; ", issues)}]"
            : $"{name}: {message}";
        return new PayPalApiException((int)statusCode, name, full.TrimStart(':', ' '), debugId, issues);
    }

    private static string? DescribeInstrument(PpCardResponse? card)
    {
        if (card is null)
        {
            return null;
        }
        var brand = string.IsNullOrWhiteSpace(card.Brand) ? "Card" : card.Brand;
        return string.IsNullOrWhiteSpace(card.LastDigits) ? brand : $"{brand} ending {card.LastDigits}";
    }

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
            ? result
            : null;

    private static string FormatReportDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "Z";
}
