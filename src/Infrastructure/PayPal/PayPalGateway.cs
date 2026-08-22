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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public PayPalGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<PayPalOptions> options,
        ILogger<PayPalGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public Task<AuthorizedPaymentResult> CreateAndAuthorizeWithCardAsync(
        int orderId,
        decimal amount,
        string currency,
        IReadOnlyList<PayPalCheckoutItem> items,
        CardPaymentSource card,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PaymentSourceRequest
        {
            Card = new CardRequest
            {
                Name = card.Name,
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                BillingAddress = MapAddress(card.BillingAddress)
            }
        };

        return CreateAndAuthorizeAsync(orderId, amount, currency, items, paymentSource, invoiceId, requestId, logBody: false, cancellationToken);
    }

    public Task<AuthorizedPaymentResult> CreateAndAuthorizeWithVaultIdAsync(
        int orderId,
        decimal amount,
        string currency,
        IReadOnlyList<PayPalCheckoutItem> items,
        string vaultId,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PaymentSourceRequest
        {
            Card = new CardRequest
            {
                VaultId = vaultId,
                StoredCredential = new StoredCredentialRequest
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "ONE_TIME",
                    Usage = "SUBSEQUENT"
                }
            }
        };

        return CreateAndAuthorizeAsync(orderId, amount, currency, items, paymentSource, invoiceId, requestId, logBody: true, cancellationToken);
    }

    private async Task<AuthorizedPaymentResult> CreateAndAuthorizeAsync(
        int orderId,
        decimal amount,
        string currency,
        IReadOnlyList<PayPalCheckoutItem> items,
        PaymentSourceRequest paymentSource,
        string invoiceId,
        string requestId,
        bool logBody,
        CancellationToken cancellationToken)
    {
        var amountValue = FormatMoney(amount);
        var body = new CreateCheckoutOrderRequest
        {
            Intent = "AUTHORIZE",
            PaymentSource = paymentSource,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                    InvoiceId = invoiceId,
                    Description = Truncate($"eShopOnWeb order {orderId}", 127),
                    Amount = new AmountRequest
                    {
                        CurrencyCode = currency,
                        Value = amountValue
                    }
                }
            }
        };

        var order = await SendAsync<PayPalOrderDto>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            body,
            requestId,
            logBody,
            cancellationToken);

        EnsureNoPayerAction(order);

        if (HasAuthorization(order))
        {
            return ToAuthorizedResult(order);
        }

        if (string.IsNullOrWhiteSpace(order.Id))
        {
            throw new PaymentException(502, "PayPal created an order without an id.");
        }

        var fetched = await SendAsync<PayPalOrderDto>(
            HttpMethod.Get,
            $"/v2/checkout/orders/{order.Id}",
            body: null,
            requestId: null,
            logBody: true,
            cancellationToken);
        EnsureNoPayerAction(fetched);
        if (HasAuthorization(fetched))
        {
            return ToAuthorizedResult(fetched);
        }

        try
        {
            var authorized = await SendAsync<PayPalOrderDto>(
                HttpMethod.Post,
                $"/v2/checkout/orders/{order.Id}/authorize",
                new AuthorizeOrderRequest { PaymentSource = paymentSource },
                $"{requestId}-authorize",
                logBody,
                cancellationToken);

            EnsureNoPayerAction(authorized);
            return ToAuthorizedResult(authorized);
        }
        catch (PaymentException ex) when (ex.Message.Contains("ORDER_ALREADY_AUTHORIZED", StringComparison.OrdinalIgnoreCase))
        {
            return await GetAuthorizedOrderAsync(order.Id, cancellationToken);
        }
    }

    private static bool HasAuthorization(PayPalOrderDto order) =>
        order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? new List<PayPalAuthorizationDto>())
            .Any(a => !string.IsNullOrWhiteSpace(a.Id)) == true;

    public async Task<AuthorizedPaymentResult> GetAuthorizedOrderAsync(
        string paypalOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await SendAsync<PayPalOrderDto>(
            HttpMethod.Get,
            $"/v2/checkout/orders/{paypalOrderId}",
            body: null,
            requestId: null,
            logBody: true,
            cancellationToken);

        return ToAuthorizedResult(order);
    }

    public async Task<AuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var auth = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            logBody: true,
            cancellationToken);

        return ToAuthorizationDetails(auth);
    }

    public async Task<AuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new MoneyRequest { CurrencyCode = currency, Value = FormatMoney(amount) }
        };

        var auth = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId,
            logBody: true,
            cancellationToken);

        return ToAuthorizationDetails(auth);
    }

    public async Task<CapturedPaymentResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new CaptureRequest
        {
            Amount = new MoneyRequest { CurrencyCode = currency, Value = FormatMoney(amount) },
            InvoiceId = invoiceId,
            FinalCapture = true
        };

        var capture = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            logBody: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(capture.Id))
        {
            throw new PaymentException(502, "PayPal captured the payment but did not return a capture id.");
        }

        var capturedAmount = ParseMoney(capture.Amount) ?? ParseMoney(capture.SellerReceivableBreakdown?.GrossAmount) ?? amount;
        var fee = ParseMoney(capture.SellerReceivableBreakdown?.PaypalFee);
        var net = ParseMoney(capture.SellerReceivableBreakdown?.NetAmount);
        var currencyCode = capture.Amount?.CurrencyCode ?? currency;

        if (capture.SellerReceivableBreakdown is null)
        {
            var details = await SendAsync<PayPalCaptureDto>(
                HttpMethod.Get,
                $"/v2/payments/captures/{capture.Id}",
                body: null,
                requestId: null,
                logBody: true,
                cancellationToken);

            capturedAmount = ParseMoney(details.Amount) ?? capturedAmount;
            fee = ParseMoney(details.SellerReceivableBreakdown?.PaypalFee) ?? fee;
            net = ParseMoney(details.SellerReceivableBreakdown?.NetAmount) ?? net;
            currencyCode = details.Amount?.CurrencyCode ?? currencyCode;
            capture.Status = details.Status ?? capture.Status;
        }

        return new CapturedPaymentResult(
            capture.Id,
            capture.Status ?? "COMPLETED",
            capturedAmount,
            fee,
            net,
            currencyCode);
    }

    public Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<object>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            body: null,
            requestId,
            logBody: true,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<RefundedPaymentResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        object body = amount.HasValue
            ? new RefundRequest { Amount = new MoneyRequest { CurrencyCode = currency, Value = FormatMoney(amount.Value) } }
            : new RefundRequest();

        var refund = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            logBody: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(refund.Id))
        {
            throw new PaymentException(502, "PayPal refunded the capture but did not return a refund id.");
        }

        return new RefundedPaymentResult(
            refund.Id,
            refund.Status ?? "COMPLETED",
            ParseMoney(refund.Amount) ?? amount ?? 0m,
            refund.Amount?.CurrencyCode ?? currency);
    }

    public async Task<VaultedCardResult> VaultCardAsync(
        CardPaymentSource card,
        string merchantCustomerId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new CreatePaymentTokenRequest
        {
            Customer = new VaultCustomerRequest { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new PaymentSourceRequest
            {
                Card = new CardRequest
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };

        var token = await SendAsync<PayPalPaymentTokenDto>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            body,
            requestId,
            logBody: false,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(token.Id))
        {
            throw new PaymentException(502, "PayPal vaulted the card but did not return a payment token id.");
        }

        return new VaultedCardResult(
            token.Id,
            token.Customer?.Id,
            token.PaymentSource?.Card?.LastDigits,
            token.PaymentSource?.Card?.Brand,
            token.PaymentSource?.Card?.Expiry,
            token.PaymentSource?.Card?.Name);
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        return SendAsync<object>(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{vaultId}",
            body: null,
            requestId: null,
            logBody: true,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from;
        while (windowStart <= to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await AddWindowAsync(results, windowStart, windowEnd, cancellationToken);
            if (windowEnd == to)
            {
                break;
            }

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
            var start = FormatTimestamp(from);
            var end = FormatTimestamp(to);
            var path =
                $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=all&page_size=100&page={page}&balance_affecting_records_only=N";

            PayPalTransactionSearchDto search;
            try
            {
                search = await SendAsync<PayPalTransactionSearchDto>(
                    HttpMethod.Get,
                    path,
                    body: null,
                    requestId: null,
                    logBody: true,
                    cancellationToken);
            }
            catch (PaymentException ex) when (ex.StatusCode == 404)
            {
                // PayPal reporting lags live activity and returns 404 when a range has no data yet.
                return;
            }

            if (search.TransactionDetails != null)
            {
                foreach (var detail in search.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null || string.IsNullOrWhiteSpace(info.TransactionId))
                    {
                        continue;
                    }

                    results.Add(new PayPalReportedTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.InvoiceId,
                        info.CustomField,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        ParseTimestamp(info.TransactionInitiationDate),
                        ParseMoney(info.FeeAmount)));
                }
            }

            totalPages = search.TotalPages > 0 ? search.TotalPages : 1;
            page++;
        } while (page <= totalPages);
    }

    private static void EnsureNoPayerAction(PayPalOrderDto order)
    {
        var needsAction = string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
                          || (order.Links?.Exists(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false);

        if (needsAction)
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires a shopper approval step (for example 3-D Secure) that cannot be completed without a browser. Direct card processing did not complete.");
        }
    }

    private static AuthorizedPaymentResult ToAuthorizedResult(PayPalOrderDto order)
    {
        var authorization = order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? new List<PayPalAuthorizationDto>())
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Id));

        if (authorization?.Id is null)
        {
            throw new PaymentException(502, "PayPal did not return an authorization id for this order.");
        }

        var amount = ParseMoney(authorization.Amount) ?? 0m;
        var currency = authorization.Amount?.CurrencyCode ?? string.Empty;
        return new AuthorizedPaymentResult(
            order.Id ?? string.Empty,
            authorization.Id,
            authorization.Status ?? "CREATED",
            ParseTimestamp(authorization.ExpirationTime),
            amount,
            currency);
    }

    private static AuthorizationDetails ToAuthorizationDetails(PayPalAuthorizationDto auth)
    {
        if (string.IsNullOrWhiteSpace(auth.Id))
        {
            throw new PaymentException(502, "PayPal did not return an authorization id.");
        }

        return new AuthorizationDetails(
            auth.Id,
            auth.Status ?? string.Empty,
            ParseTimestamp(auth.ExpirationTime),
            ParseMoney(auth.Amount),
            auth.Amount?.CurrencyCode);
    }

    private static BillingAddressRequest MapAddress(CardBillingAddress address) => new()
    {
        CountryCode = address.CountryCode,
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.AdminArea2,
        AdminArea1 = address.AdminArea1,
        PostalCode = address.PostalCode
    };

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        bool logBody,
        CancellationToken cancellationToken,
        bool allowEmpty = false) where T : class
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0)
            {
                var delayMs = (int)(Math.Pow(2, attempt) * 200 + Random.Shared.Next(0, 200));
                await Task.Delay(delayMs, cancellationToken);
            }

            try
            {
                return await SendOnceAsync<T>(method, path, body, requestId, logBody, cancellationToken, allowEmpty);
            }
            catch (PaymentException ex) when (attempt < 3 && (ex.StatusCode == 429 || ex.StatusCode >= 500))
            {
                lastError = ex;
                _logger.LogWarning("PayPal {Method} {Path} failed with {Status}; retrying. {Message}", method, path, ex.StatusCode, ex.Message);
            }
        }

        throw lastError ?? new PaymentException(502, "PayPal request failed.");
    }

    private async Task<T> SendOnceAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        bool logBody,
        CancellationToken cancellationToken,
        bool allowEmpty) where T : class
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        var client = _httpClientFactory.CreateClient("PayPal");
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (method == HttpMethod.Post)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body is not null && method != HttpMethod.Get)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal {Method} {Path} transport failure. Debug correlation unavailable.", method, path);
            throw new PaymentException(502, "Unable to reach PayPal.");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _accessToken = null;
            }

            var error = TryParseError(payload);
            var issueParts = new List<string>();
            if (error?.Details is { Count: > 0 })
            {
                foreach (var d in error.Details)
                {
                    var part = d.Issue ?? "issue";
                    if (!string.IsNullOrWhiteSpace(d.Field))
                    {
                        part += $" (field={d.Field})";
                    }

                    if (!string.IsNullOrWhiteSpace(d.Description))
                    {
                        part += $": {d.Description}";
                    }

                    issueParts.Add(part);
                }
            }

            var issue = issueParts.Count > 0
                ? string.Join("; ", issueParts)
                : error?.Message ?? response.ReasonPhrase ?? "PayPal request failed.";

            _logger.LogWarning(
                "PayPal {Method} {Path} returned {Status}. name={Name} debug_id={DebugId} issue={Issue}",
                method, path, (int)response.StatusCode, error?.Name, error?.DebugId, issue);

            if (string.Equals(error?.Name, "UNPROCESSABLE_ENTITY", StringComparison.OrdinalIgnoreCase)
                && issue.Contains("PAYER_ACTION", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentChallengeRequiredException(
                    "PayPal requires a shopper approval step that cannot be completed without a browser.");
            }

            var status = (int)response.StatusCode;
            if (status == 404)
            {
                throw new PaymentException(404, $"PayPal resource not found. {issue}");
            }

            throw new PaymentException(status >= 400 && status < 600 ? status : 502,
                $"PayPal error {error?.Name ?? response.StatusCode.ToString()}: {issue} (debug_id={error?.DebugId})");
        }

        if (allowEmpty && string.IsNullOrWhiteSpace(payload))
        {
            return Activator.CreateInstance<T>();
        }

        if (logBody)
        {
            _logger.LogInformation("PayPal {Method} {Path} succeeded with {Status}.", method, path, (int)response.StatusCode);
        }
        else
        {
            _logger.LogInformation("PayPal {Method} {Path} succeeded with {Status}. Request body omitted because it contained card data.", method, path, (int)response.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Activator.CreateInstance<T>();
        }

        var parsed = JsonSerializer.Deserialize<T>(payload, JsonOptions);
        if (parsed is null)
        {
            throw new PaymentException(502, "PayPal returned an empty or unreadable response.");
        }

        EnsureNoPayerActionFromPayload(payload);
        return parsed;
    }

    private static void EnsureNoPayerActionFromPayload(string payload)
    {
        if (payload.Contains("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || payload.Contains("\"rel\":\"payer-action\"", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires a shopper approval step (for example 3-D Secure) that cannot be completed without a browser. Direct card processing did not complete.");
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new PaymentException(500, "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret.");
            }

            var client = _httpClientFactory.CreateClient("PayPal");
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PayPal token request transport failure.");
                throw new PaymentException(502, "Unable to reach PayPal to obtain an access token.");
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = TryParseError(payload);
                _logger.LogWarning("PayPal token request failed with {Status}. debug_id={DebugId}", (int)response.StatusCode, error?.DebugId);
                throw new PaymentException(502, "PayPal refused the client-credentials token request.");
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(payload, JsonOptions);
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                throw new PaymentException(502, "PayPal token response did not include an access_token.");
            }

            _accessToken = token.AccessToken;
            var lifetime = token.ExpiresIn > 0 ? token.ExpiresIn : 300;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, lifetime - 60));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static PayPalErrorBody? TryParseError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<PayPalErrorBody>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(PayPalMoneyDto? money)
    {
        if (money?.Value is null)
        {
            return null;
        }

        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? decimal.Round(value, 2, MidpointRounding.AwayFromZero)
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
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
