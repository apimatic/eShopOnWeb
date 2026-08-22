using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan TokenSkew = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxReportingWindow = TimeSpan.FromDays(31);

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalOptions> options, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl().TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public string Currency
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.Currency))
            {
                throw new PaymentException(500, "PayPal:Currency is not configured.", "PAYPAL_NOT_CONFIGURED");
            }

            return _options.Currency.Trim().ToUpperInvariant();
        }
    }

    public Task<AuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSource
        {
            Card = new PayPalCardSource
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = string.IsNullOrWhiteSpace(card.Name) ? "Jane Doe" : card.Name,
                BillingAddress = MapAddress(card.BillingAddress) ?? DefaultBillingAddress()
            }
        };

        return AuthorizeAsync(orderId, amount, paymentSource, idempotencyKey, cancellationToken);
    }

    public Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSource
        {
            Card = new PayPalCardSource { VaultId = vaultId }
        };

        return AuthorizeAsync(orderId, amount, paymentSource, idempotencyKey, cancellationToken);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalReauthorizeRequest
        {
            Amount = Money(amount)
        };

        var authorization = await SendAsync<PayPalAuthorization>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            idempotencyKey,
            cancellationToken);

        if (authorization == null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PaymentException(502, "PayPal reauthorization did not return an authorization id.", "PAYPAL_AUTHORIZATION_MISSING");
        }

        return MapAuthorization(paypalOrderId: string.Empty, authorization, amount);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<PayPalAuthorization>(
                HttpMethod.Post,
                $"v2/payments/authorizations/{authorizationId}/void",
                body: new { },
                idempotencyKey,
                cancellationToken,
                allowNoContent: true);
        }
        catch (PaymentException ex) when (
            string.Equals(ex.ErrorCode, "AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ex.ErrorCode, "AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
            || ex.StatusCode == 404)
        {
            // Already released — cancel is idempotent.
        }
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCaptureRequest
        {
            Amount = Money(amount),
            FinalCapture = true
        };

        var capture = await SendAsync<PayPalCapture>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            body,
            idempotencyKey,
            cancellationToken);

        if (capture == null || string.IsNullOrEmpty(capture.Id))
        {
            throw new PaymentException(502, "PayPal capture response did not include a capture id.", "PAYPAL_CAPTURE_MISSING");
        }

        var capturedAmount = ParseMoney(capture.Amount) ?? amount;
        var fee = ParseMoney(capture.SellerReceivableBreakdown?.PaypalFee) ?? 0m;
        var net = ParseMoney(capture.SellerReceivableBreakdown?.NetAmount)
            ?? capturedAmount - fee;

        return new CaptureResult(
            capture.Id,
            capture.Status ?? "COMPLETED",
            capturedAmount,
            fee,
            net,
            capture.Amount?.CurrencyCode ?? Currency);
    }

    public async Task<RefundGatewayResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        object body = amount.HasValue
            ? new PayPalRefundRequest { Amount = Money(amount.Value) }
            : new { };

        var refund = await SendAsync<PayPalRefund>(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            body,
            idempotencyKey,
            cancellationToken);

        if (refund == null || string.IsNullOrEmpty(refund.Id))
        {
            throw new PaymentException(502, "PayPal refund response did not include a refund id.", "PAYPAL_REFUND_MISSING");
        }

        return new RefundGatewayResult(
            refund.Id,
            refund.Status ?? "COMPLETED",
            ParseMoney(refund.Amount) ?? amount ?? 0m,
            refund.Amount?.CurrencyCode ?? Currency);
    }

    public async Task<VaultedCardResult> VaultCardAsync(
        CardPaymentDetails card,
        string? existingPayPalCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var setupRequest = new PayPalSetupTokenRequest
        {
            Customer = string.IsNullOrWhiteSpace(existingPayPalCustomerId)
                ? null
                : new PayPalVaultCustomer { Id = existingPayPalCustomerId },
            PaymentSource = new PayPalSetupPaymentSource
            {
                Card = new PayPalSetupCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = string.IsNullOrWhiteSpace(card.Name) ? "Jane Doe" : card.Name,
                    BillingAddress = MapAddress(card.BillingAddress) ?? DefaultBillingAddress()
                }
            }
        };

        var setup = await SendAsync<PayPalSetupTokenResponse>(
            HttpMethod.Post,
            "v3/vault/setup-tokens",
            setupRequest,
            idempotencyKey,
            cancellationToken);

        if (setup == null || string.IsNullOrEmpty(setup.Id))
        {
            throw new PaymentException(502, "PayPal did not return a setup token id.", "PAYPAL_VAULT_FAILED");
        }

        EnsureNoPayerAction(setup.Status, setup.Links);

        if (!string.Equals(setup.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(setup.Status))
        {
            throw new PaymentException(502,
                $"PayPal setup token status was '{setup.Status}', expected APPROVED.",
                "PAYPAL_VAULT_NOT_APPROVED");
        }

        var tokenRequest = new PayPalPaymentTokenRequest
        {
            PaymentSource = new PayPalTokenPaymentSource
            {
                Token = new PayPalTokenRef { Id = setup.Id, Type = "SETUP_TOKEN" }
            }
        };

        var paymentToken = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            tokenRequest,
            idempotencyKey + "-token",
            cancellationToken);

        if (paymentToken == null || string.IsNullOrEmpty(paymentToken.Id))
        {
            throw new PaymentException(502, "PayPal did not return a payment token id.", "PAYPAL_VAULT_FAILED");
        }

        var vaultedCard = paymentToken.PaymentSource?.Card ?? setup.PaymentSource?.Card;
        return new VaultedCardResult(
            paymentToken.Id,
            paymentToken.Customer?.Id ?? setup.Customer?.Id,
            vaultedCard?.LastDigits ?? LastDigitsOf(card.Number),
            vaultedCard?.Brand ?? "CARD",
            vaultedCard?.Expiry ?? card.Expiry,
            vaultedCard?.Name ?? card.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<PayPalPaymentTokenResponse>(
                HttpMethod.Delete,
                $"v3/vault/payment-tokens/{vaultId}",
                body: null,
                requestId: null,
                cancellationToken,
                allowNoContent: true);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // Already removed at PayPal.
        }
    }

    public async Task<IReadOnlyList<ReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReportedTransaction>();
        var cursor = from;
        while (cursor < to)
        {
            var chunkEnd = cursor.Add(MaxReportingWindow);
            if (chunkEnd > to)
            {
                chunkEnd = to;
            }

            await ListTransactionPagesAsync(cursor, chunkEnd, results, cancellationToken);
            cursor = chunkEnd;
        }

        return results;
    }

    private async Task ListTransactionPagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        List<ReportedTransaction> sink,
        CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var start = FormatReportingDate(from);
            var end = FormatReportingDate(to);
            var path =
                $"v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&page_size=500&page={page}&fields=all&balance_affecting_records_only=N";

            var pageResult = await SendAsync<PayPalTransactionSearchResponse>(
                HttpMethod.Get,
                path,
                body: null,
                requestId: null,
                cancellationToken);

            if (pageResult?.TransactionDetails != null)
            {
                foreach (var detail in pageResult.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info == null || string.IsNullOrEmpty(info.TransactionId))
                    {
                        continue;
                    }

                    sink.Add(new ReportedTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.PaypalReferenceIdType,
                        info.InvoiceId,
                        info.CustomField,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        ParseTime(info.TransactionInitiationDate),
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        ParseMoney(info.FeeAmount)));
                }
            }

            totalPages = pageResult?.TotalPages > 0 ? pageResult.TotalPages : 1;
            page++;
        } while (page <= totalPages);
    }

    private async Task<AuthorizationResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        PayPalPaymentSource paymentSource,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var customId = OrderCheckoutService.InvoiceId(orderId);
        var invoiceId = $"{customId}-{Guid.NewGuid():N}";
        var request = new PayPalOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits =
            {
                new PayPalPurchaseUnitRequest
                {
                    ReferenceId = customId,
                    InvoiceId = invoiceId,
                    CustomId = customId,
                    Amount = Money(amount)
                }
            },
            PaymentSource = paymentSource
        };

        var created = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            "v2/checkout/orders",
            request,
            idempotencyKey,
            cancellationToken);

        if (created == null || string.IsNullOrEmpty(created.Id))
        {
            throw new PaymentException(502, "PayPal did not return an order id.", "PAYPAL_ORDER_MISSING");
        }

        EnsureNoPayerAction(created.Status, created.Links);

        var authorization = ExtractAuthorization(created);
        if (authorization == null)
        {
            created = await SendAsync<PayPalOrderResponse>(
                HttpMethod.Post,
                $"v2/checkout/orders/{created.Id}/authorize",
                body: new { },
                requestId: idempotencyKey + "-authorize",
                cancellationToken);
            EnsureNoPayerAction(created?.Status, created?.Links);
            authorization = ExtractAuthorization(created);
        }

        if (authorization == null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PaymentException(502, "PayPal did not return an authorization id.", "PAYPAL_AUTHORIZATION_MISSING");
        }

        return MapAuthorization(created!.Id!, authorization, amount);
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowNoContent = false) where T : class
    {
        EnsureConfigured();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = new HttpRequestMessage(method, relativePath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrEmpty(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            if (body != null && method != HttpMethod.Get && method != HttpMethod.Delete)
            {
                var json = JsonSerializer.Serialize(body, PayPalJson.Options);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PayPal {Method} {Path} failed to send.", method, SanitizePath(relativePath));
                throw new PaymentException(502, "Unable to reach PayPal.", "PAYPAL_UNREACHABLE");
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var debugId = TryGetHeader(response, "Paypal-Debug-Id") ?? TryGetHeader(response, "PayPal-Debug-Id");

            _logger.LogInformation(
                "PayPal {Method} {Path} -> {StatusCode} debug_id={DebugId}",
                method,
                SanitizePath(relativePath),
                (int)response.StatusCode,
                debugId);

            if (response.StatusCode == HttpStatusCode.Conflict && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                if (allowNoContent || response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(payload))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<T>(payload, PayPalJson.Options);
            }

            throw MapPayPalError(response.StatusCode, payload, debugId);
        }

        throw new PaymentException(409, "PayPal reported a conflicting in-progress request. Retry shortly.", "PAYPAL_CONFLICT");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            EnsureConfigured();

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var raw = $"{_options.ClientId}:{_options.ClientSecret}";
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed with {StatusCode}.", (int)response.StatusCode);
                throw new PaymentException(502, "PayPal authentication failed.", "PAYPAL_AUTH_FAILED");
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(payload, PayPalJson.Options);
            if (token == null || string.IsNullOrEmpty(token.AccessToken))
            {
                throw new PaymentException(502, "PayPal authentication failed.", "PAYPAL_AUTH_FAILED");
            }

            _accessToken = token.AccessToken;
            var lifetime = token.ExpiresIn > 0 ? TimeSpan.FromSeconds(token.ExpiresIn) : TimeSpan.FromHours(8);
            _tokenExpiresAt = DateTimeOffset.UtcNow.Add(lifetime) - TokenSkew;
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new PaymentException(500, "PayPal client credentials are not configured.", "PAYPAL_NOT_CONFIGURED");
        }
    }

    private static PaymentException MapPayPalError(HttpStatusCode statusCode, string payload, string? debugId)
    {
        PayPalErrorResponse? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorResponse>(payload, PayPalJson.Options);
        }
        catch (JsonException)
        {
            // Fall through with a generic message. Never include the raw payload — it may echo card data.
        }

        var issue = error?.Details?.Find(d => !string.IsNullOrEmpty(d.Issue))?.Issue;
        var description = error?.Details?.Find(d => !string.IsNullOrEmpty(d.Description))?.Description;
        var message = description ?? error?.Message ?? "PayPal rejected the request.";
        if (!string.IsNullOrEmpty(debugId))
        {
            message = $"{message} (PayPal debug id: {debugId})";
        }

        var http = statusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            (HttpStatusCode)422 => 409,
            _ => 502
        };

        return new PaymentException(http, message, issue ?? error?.Name);
    }

    private static void EnsureNoPayerAction(string? status, List<PayPalLink>? links)
    {
        var needsAction = string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || links?.Exists(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase)) == true;

        if (needsAction)
        {
            throw new PaymentException(502,
                "PayPal required a shopper to approve this card in a browser (3-D Secure / payer-action). This integration does not perform an approval round-trip.",
                "PAYER_ACTION_REQUIRED");
        }
    }

    private static PayPalAuthorization? ExtractAuthorization(PayPalOrderResponse? order) =>
        order?.PurchaseUnits?.Find(u => u.Payments?.Authorizations?.Count > 0)?.Payments?.Authorizations?[0];

    private AuthorizationResult MapAuthorization(string paypalOrderId, PayPalAuthorization authorization, decimal fallbackAmount)
    {
        var authorizedAmount = ParseMoney(authorization.Amount) ?? fallbackAmount;
        return new AuthorizationResult(
            paypalOrderId,
            authorization.Id!,
            authorization.Status ?? "CREATED",
            authorizedAmount,
            authorization.Amount?.CurrencyCode ?? Currency,
            ParseTime(authorization.CreateTime),
            ParseTime(authorization.ExpirationTime));
    }

    private PayPalMoney Money(decimal amount) => new()
    {
        CurrencyCode = Currency,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static PayPalAddress DefaultBillingAddress() => new()
    {
        AddressLine1 = "123 Main St.",
        AdminArea1 = "CA",
        AdminArea2 = "Anytown",
        PostalCode = "12345",
        CountryCode = "US"
    };

    private static PayPalAddress? MapAddress(CardBillingAddress? address)
    {
        if (address == null)
        {
            return null;
        }

        return new PayPalAddress
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static decimal? ParseMoney(PayPalMoney? money)
    {
        if (money?.Value == null)
        {
            return null;
        }

        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static string FormatReportingDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string LastDigitsOf(string number)
    {
        var digits = number.Replace(" ", string.Empty);
        return digits.Length <= 4 ? digits : digits[^4..];
    }

    private static string SanitizePath(string path)
    {
        var query = path.IndexOf('?', StringComparison.Ordinal);
        return query >= 0 ? path[..query] : path;
    }

    private static string? TryGetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}
