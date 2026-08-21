using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "HUF", "TWD", "KRW"
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalPaymentGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public PayPalPaymentGateway(HttpClient httpClient, IOptions<PayPalOptions> options, ILogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl().TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<PaymentAuthorizationResult> AuthorizeAsync(AuthorizePaymentCommand command, CancellationToken cancellationToken = default)
    {
        var createBody = BuildCreateOrderBody(command);
        var created = await SendAsync<PayPalOrder>(
            HttpMethod.Post,
            "v2/checkout/orders",
            createBody,
            command.IdempotencyKey,
            cancellationToken);

        EnsureNoPayerAction(created);

        var authorization = FirstAuthorization(created);
        var order = created;
        if (authorization is null)
        {
            try
            {
                order = await SendAsync<PayPalOrder>(
                    HttpMethod.Post,
                    $"v2/checkout/orders/{created.Id}/authorize",
                    new { },
                    $"{command.IdempotencyKey}-auth",
                    cancellationToken);
            }
            catch (PaymentException ex) when (
                string.Equals(ex.ErrorCode, "ORDER_ALREADY_AUTHORIZED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ex.ErrorCode, "ORDER_ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ex.ErrorCode, "ORDER_NOT_APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                order = await SendAsync<PayPalOrder>(
                    HttpMethod.Get,
                    $"v2/checkout/orders/{created.Id}",
                    body: null,
                    requestId: null,
                    cancellationToken);
            }

            EnsureNoPayerAction(order);
            authorization = FirstAuthorization(order);
        }

        if (authorization?.Id is null)
        {
            throw new PaymentException(
                $"PayPal did not return an authorization for order {order.Id}. Status: {order.Status}.",
                502,
                "AUTHORIZATION_MISSING");
        }

        var amount = ParseMoney(authorization.Amount) ?? command.Amount;
        return new PaymentAuthorizationResult(
            order.Id!,
            order.Status,
            authorization.Id,
            authorization.Status,
            ParseTimestamp(authorization.ExpirationTime),
            amount,
            authorization.Amount?.CurrencyCode ?? command.Currency);
    }

    public async Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<PayPalAuthorization>(
            HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);
        return ToSnapshot(authorization);
    }

    public async Task<AuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new { amount = Money(amount, currency) };
        var authorization = await SendAsync<PayPalAuthorization>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            idempotencyKey,
            cancellationToken);
        return ToSnapshot(authorization);
    }

    public async Task<PaymentCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = Money(amount, currency),
            invoice_id = invoiceId,
            final_capture = true
        };

        var capture = await SendAsync<PayPalCapture>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            body,
            idempotencyKey,
            cancellationToken);

        if (string.IsNullOrEmpty(capture.Id))
        {
            throw new PaymentException("PayPal did not return a capture id.", 502, "CAPTURE_MISSING");
        }

        var capturedAmount = ParseMoney(capture.SellerReceivableBreakdown?.GrossAmount)
            ?? ParseMoney(capture.Amount)
            ?? amount;
        var fee = ParseMoney(capture.SellerReceivableBreakdown?.PaypalFee);
        var net = ParseMoney(capture.SellerReceivableBreakdown?.NetAmount);

        return new PaymentCaptureResult(
            capture.Id,
            capture.Status,
            capturedAmount,
            fee,
            net,
            capture.Amount?.CurrencyCode ?? currency,
            authorizationId,
            "CAPTURED");
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<PayPalAuthorization>(
                HttpMethod.Post,
                $"v2/payments/authorizations/{authorizationId}/void",
                body: null,
                idempotencyKey,
                cancellationToken,
                allowNoContent: true);
        }
        catch (PaymentException ex) when (
            string.Equals(ex.ErrorCode, "AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ex.ErrorCode, "PREVIOUSLY_VOIDED", StringComparison.OrdinalIgnoreCase)
            || (ex.Message?.Contains("VOIDED", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            _logger.LogInformation("Authorization {AuthorizationId} was already voided.", authorizationId);
        }
    }

    public async Task<PaymentRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        object body = amount.HasValue
            ? new { amount = Money(amount.Value, currency) }
            : new { };

        var refund = await SendAsync<PayPalRefund>(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            body,
            TruncateRequestId(idempotencyKey),
            cancellationToken);

        if (string.IsNullOrEmpty(refund.Id))
        {
            throw new PaymentException("PayPal did not return a refund id.", 502, "REFUND_MISSING");
        }

        return new PaymentRefundResult(
            refund.Id,
            refund.Status,
            ParseMoney(refund.Amount) ?? amount ?? 0m,
            refund.Amount?.CurrencyCode ?? currency);
    }

    public async Task<VaultedCardResult> VaultCardAsync(
        string merchantCustomerId,
        CardDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            customer = new { merchant_customer_id = merchantCustomerId },
            payment_source = new
            {
                card = new
                {
                    name = card.Name,
                    number = card.Number,
                    expiry = card.Expiry,
                    security_code = card.SecurityCode,
                    billing_address = BillingAddress(card)
                }
            }
        };

        var token = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            body,
            TruncateRequestId(idempotencyKey),
            cancellationToken);

        if (string.IsNullOrEmpty(token.Id))
        {
            throw new PaymentException("PayPal did not return a payment token id.", 502, "VAULT_MISSING");
        }

        var cardResponse = token.PaymentSource?.Card;
        return new VaultedCardResult(
            token.Id,
            token.Customer?.Id,
            cardResponse?.Brand ?? "UNKNOWN",
            cardResponse?.LastDigits ?? card.LastDigits,
            cardResponse?.Expiry ?? card.Expiry,
            cardResponse?.Name ?? card.Name);
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(
            HttpMethod.Delete,
            $"v3/vault/payment-tokens/{paymentTokenId}",
            body: null,
            requestId: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();
        foreach (var (chunkFrom, chunkTo) in SplitInto31DayWindows(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                var start = FormatTimestamp(chunkFrom);
                var end = FormatTimestamp(chunkTo);
                var path =
                    $"v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&page_size=500&page={page}&fields=transaction_info&balance_affecting_records_only=N";

                var response = await SendAsync<PayPalTransactionSearchResponse>(
                    HttpMethod.Get,
                    path,
                    body: null,
                    requestId: null,
                    cancellationToken);

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info?.TransactionId is null)
                        {
                            continue;
                        }

                        results.Add(new GatewayTransaction(
                            info.TransactionId,
                            info.PaypalReferenceId,
                            info.InvoiceId,
                            info.CustomField,
                            info.TransactionEventCode,
                            info.TransactionStatus,
                            ParseMoney(info.TransactionAmount),
                            info.TransactionAmount?.CurrencyCode,
                            ParseTimestamp(info.TransactionInitiationDate)));
                    }
                }

                totalPages = response.TotalPages.GetValueOrDefault(1);
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private object BuildCreateOrderBody(AuthorizePaymentCommand command)
    {
        object cardSource;
        if (!string.IsNullOrEmpty(command.VaultId))
        {
            cardSource = new
            {
                vault_id = command.VaultId,
                stored_credential = new
                {
                    payment_initiator = "CUSTOMER",
                    payment_type = "UNSCHEDULED",
                    usage = "SUBSEQUENT"
                }
            };
        }
        else
        {
            var card = command.Card ?? throw new PaymentException("Card details are required when no saved card is specified.", 400, "INVALID_PAYMENT_SOURCE");
            cardSource = new
            {
                name = card.Name,
                number = card.Number,
                expiry = card.Expiry,
                security_code = card.SecurityCode,
                billing_address = BillingAddress(card)
            };
        }

        return new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = "default",
                    custom_id = command.OrderId.ToString(CultureInfo.InvariantCulture),
                    invoice_id = command.InvoiceId,
                    description = $"eShopOnWeb order {command.OrderId}",
                    amount = Money(command.Amount, command.Currency)
                }
            },
            payment_source = new { card = cardSource }
        };
    }

    private static object BillingAddress(CardDetails card) => new
    {
        address_line_1 = card.BillingAddress.AddressLine1,
        admin_area_2 = card.BillingAddress.AdminArea2,
        admin_area_1 = card.BillingAddress.AdminArea1,
        postal_code = card.BillingAddress.PostalCode,
        country_code = card.BillingAddress.CountryCode
    };

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowNoContent = false)
    {
        await EnsureAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", TruncateRequestId(requestId));
        }

        if (body is not null && method != HttpMethod.Get)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method.Method, SanitizePath(relativePath));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new PaymentException($"PayPal request failed: {ex.Message}", 502, "PAYPAL_UNAVAILABLE", ex);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            InvalidateToken();
            await EnsureAccessTokenAsync(cancellationToken);
            using var retry = new HttpRequestMessage(method, relativePath);
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            retry.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                retry.Headers.TryAddWithoutValidation("PayPal-Request-Id", TruncateRequestId(requestId));
            }

            if (body is not null && method != HttpMethod.Get)
            {
                retry.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            }

            response.Dispose();
            response = await _httpClient.SendAsync(retry, cancellationToken);
            payload = await response.Content.ReadAsStringAsync(cancellationToken);
        }

        using (response)
        {
            if (allowNoContent && (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(payload)))
            {
                if (!response.IsSuccessStatusCode)
                {
                    ThrowPayPalError(response.StatusCode, payload);
                }

                return default!;
            }

            if (!response.IsSuccessStatusCode)
            {
                ThrowPayPalError(response.StatusCode, payload);
            }

            if (string.IsNullOrWhiteSpace(payload) || typeof(T) == typeof(object))
            {
                return default!;
            }

            var parsed = JsonSerializer.Deserialize<T>(payload, JsonOptions);
            if (parsed is null)
            {
                throw new PaymentException("PayPal returned an empty response body.", 502, "PAYPAL_EMPTY_RESPONSE");
            }

            return parsed;
        }
    }

    private async Task EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_accessToken) && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new PaymentException(
                    "PayPal credentials are not configured. Set PAYPAL_CLIENT_ID and PAYPAL_CLIENT_SECRET (PayPal:ClientId / PayPal:ClientSecret).",
                    500,
                    "PAYPAL_NOT_CONFIGURED");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            _logger.LogInformation("PayPal POST v1/oauth2/token");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                ThrowPayPalError(response.StatusCode, payload);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(payload, JsonOptions)
                ?? throw new PaymentException("PayPal token response was empty.", 502, "PAYPAL_AUTH_FAILED");
            if (string.IsNullOrEmpty(token.AccessToken))
            {
                throw new PaymentException("PayPal token response did not include an access token.", 502, "PAYPAL_AUTH_FAILED");
            }

            _accessToken = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 30));
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateToken()
    {
        _accessToken = null;
        _tokenExpiresAt = DateTimeOffset.MinValue;
    }

    private void ThrowPayPalError(HttpStatusCode statusCode, string payload)
    {
        PayPalError? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalError>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            // Use the raw status when PayPal's body is not the documented error schema.
        }

        var issue = error?.Details is { Length: > 0 } ? error.Details[0].Issue : error?.Name;
        var description = error?.Details is { Length: > 0 } ? error.Details[0].Description : error?.Message;
        var message = string.IsNullOrWhiteSpace(description)
            ? $"PayPal request failed with HTTP {(int)statusCode}."
            : description;

        if (!string.IsNullOrWhiteSpace(error?.DebugId))
        {
            message = $"{message} (debug_id {error.DebugId})";
        }

        if (string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(error?.Name, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper to approve this card payment in a browser. Direct card processing without a browser challenge is not available for this account or card.");
        }

        var mappedStatus = statusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 403,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            (HttpStatusCode)422 => 409,
            _ => 502
        };

        _logger.LogWarning("PayPal error {Status} {Issue}: {Message}", (int)statusCode, issue, message);
        throw new PaymentException(message, mappedStatus, issue);
    }

    private static void EnsureNoPayerAction(PayPalOrder order)
    {
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper to approve this card payment in a browser. Direct card processing without a browser challenge is not available for this account or card.");
        }

        if (order.Links is null)
        {
            return;
        }

        foreach (var link in order.Links)
        {
            if (string.Equals(link.Rel, "payer-action", StringComparison.OrdinalIgnoreCase))
            {
                throw new PayerActionRequiredException(
                    "PayPal required a shopper to approve this card payment in a browser. Direct card processing without a browser challenge is not available for this account or card.");
            }
        }
    }

    private static PayPalAuthorization? FirstAuthorization(PayPalOrder order)
    {
        var units = order.PurchaseUnits;
        if (units is null)
        {
            return null;
        }

        foreach (var unit in units)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is { Length: > 0 } && !string.IsNullOrEmpty(authorizations[0].Id))
            {
                return authorizations[0];
            }
        }

        return null;
    }

    private static AuthorizationSnapshot ToSnapshot(PayPalAuthorization authorization)
    {
        if (string.IsNullOrEmpty(authorization.Id))
        {
            throw new PaymentException("PayPal authorization was missing an id.", 502, "AUTHORIZATION_MISSING");
        }

        return new AuthorizationSnapshot(
            authorization.Id,
            authorization.Status,
            ParseTimestamp(authorization.ExpirationTime),
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode);
    }

    private static object Money(decimal amount, string currency) => new
    {
        currency_code = currency,
        value = FormatAmount(amount, currency)
    };

    private static string FormatAmount(decimal amount, string currency)
    {
        if (ZeroDecimalCurrencies.Contains(currency))
        {
            return decimal.Truncate(amount).ToString("0", CultureInfo.InvariantCulture);
        }

        return amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static decimal? ParseMoney(PayPalMoney? money)
    {
        if (money?.Value is null)
        {
            return null;
        }

        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
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

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string TruncateRequestId(string requestId)
    {
        if (string.IsNullOrEmpty(requestId))
        {
            return requestId;
        }

        return requestId.Length <= 108 ? requestId : requestId[..108];
    }

    private static string SanitizePath(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? path[..q] : path;
    }

    private static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> SplitInto31DayWindows(DateTimeOffset from, DateTimeOffset to)
    {
        var window = TimeSpan.FromDays(31);
        var cursor = from;
        while (cursor < to)
        {
            var end = cursor + window;
            if (end > to)
            {
                end = to;
            }

            // Exclusive overlap: keep at least one second of range.
            if (end <= cursor)
            {
                end = to;
            }

            yield return (cursor, end);
            cursor = end;
        }
    }
}
