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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public sealed class PayPalPaymentsClient : IPayPalPaymentsClient
{
    private const string TokenCacheKey = "paypal:oauth-access-token";
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "HUF", "TWD", "VND"
    };

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPalPaymentsClient> _logger;
    private readonly PayPalSettings _settings;
    private readonly string _baseUrl;

    public PayPalPaymentsClient(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<PayPalSettings> options,
        ILogger<PayPalPaymentsClient> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
        _settings = options.Value;
        _baseUrl = ResolveBaseUrl(_settings);
        _httpClient.BaseAddress = new Uri(_baseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(45);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string Currency
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_settings.Currency))
            {
                throw new PaymentOperationException(500, "PayPal:Currency is not configured.");
            }

            return _settings.Currency.Trim().ToUpperInvariant();
        }
    }

    public string FormatMoney(decimal amount)
    {
        if (ZeroDecimalCurrencies.Contains(Currency))
        {
            return decimal.Truncate(amount).ToString("0", CultureInfo.InvariantCulture);
        }

        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
    }

    public decimal ParseMoney(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        int orderId,
        decimal amount,
        IReadOnlyList<PayPalPurchaseItem> items,
        CardPaymentDetails card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSourceRequest
        {
            Card = BuildCardRequest(card, vaultId: null, storedCredential: null)
        };

        return AuthorizeAsync(orderId, amount, items, paymentSource, requestId, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardPaymentAsync(
        int orderId,
        decimal amount,
        IReadOnlyList<PayPalPurchaseItem> items,
        string vaultId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSourceRequest
        {
            Card = new PayPalCardRequest
            {
                VaultId = vaultId,
                StoredCredential = new PayPalStoredCredential
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "UNSCHEDULED",
                    Usage = "SUBSEQUENT"
                }
            }
        };

        return AuthorizeAsync(orderId, amount, items, paymentSource, requestId, cancellationToken);
    }

    public async Task<PayPalAuthorizationResult?> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        var resource = Deserialize<PayPalAuthorizationResource>(response.Json);
        if (resource?.Id is null)
        {
            return null;
        }

        return MapAuthorization(paypalOrderId: string.Empty, orderStatus: resource.Status ?? string.Empty, resource);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalReauthorizeRequest
        {
            Amount = new PayPalMoneyDto { CurrencyCode = Currency, Value = FormatMoney(amount) }
        };

        try
        {
            var response = await SendAsync(
                HttpMethod.Post,
                $"v2/payments/authorizations/{authorizationId}/reauthorize",
                body,
                requestId,
                cancellationToken);

            var resource = Deserialize<PayPalAuthorizationResource>(response.Json)
                           ?? throw new PaymentOperationException(502, "PayPal reauthorize returned an empty body.", response.DebugId);

            if (string.IsNullOrEmpty(resource.Id))
            {
                throw new PaymentOperationException(502, "PayPal reauthorize did not return an authorization id.", response.DebugId);
            }

            return MapAuthorization(string.Empty, resource.Status ?? string.Empty, resource);
        }
        catch (PaymentOperationException ex) when (IsAuthorizationUnrenewable(ex))
        {
            throw new PaymentOperationException(409,
                "This authorization has expired and cannot be renewed. PayPal authorizations can only be refreshed within 29 days of the original hold. Ask the shopper to pay the order again so a new hold can be placed.",
                ex.PayPalDebugId);
        }
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        bool finalCapture,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCaptureRequest
        {
            Amount = new PayPalMoneyDto { CurrencyCode = Currency, Value = FormatMoney(amount) },
            FinalCapture = finalCapture
        };

        var response = await SendAsync(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            cancellationToken);

        var capture = Deserialize<PayPalCaptureResource>(response.Json)
                      ?? throw new PaymentOperationException(502, "PayPal capture returned an empty body.", response.DebugId);

        if (capture.SellerReceivableBreakdown is null && !string.IsNullOrEmpty(capture.Id))
        {
            var details = await SendAsync(
                HttpMethod.Get,
                $"v2/payments/captures/{capture.Id}",
                body: null,
                requestId: null,
                cancellationToken);
            capture = Deserialize<PayPalCaptureResource>(details.Json) ?? capture;
        }

        var capturedAmount = ParseMoney(capture.SellerReceivableBreakdown?.GrossAmount?.Value ?? capture.Amount?.Value);
        var fee = capture.SellerReceivableBreakdown?.PaypalFee?.Value is string feeValue ? ParseMoney(feeValue) : (decimal?)null;
        var net = capture.SellerReceivableBreakdown?.NetAmount?.Value is string netValue ? ParseMoney(netValue) : (decimal?)null;

        return new PayPalCaptureResult
        {
            CaptureId = capture.Id ?? throw new PaymentOperationException(502, "PayPal capture did not return an id.", response.DebugId),
            Status = capture.Status ?? string.Empty,
            CapturedAmount = capturedAmount,
            PayPalFee = fee,
            NetAmount = net,
            Currency = capture.Amount?.CurrencyCode ?? Currency
        };
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(
                HttpMethod.Post,
                $"v2/payments/authorizations/{authorizationId}/void",
                body: null,
                requestId,
                cancellationToken,
                allowEmptyBody: true);
        }
        catch (PaymentOperationException ex) when (IsAlreadyVoided(ex))
        {
            _logger.LogInformation("PayPal authorization {AuthorizationId} was already voided (debug_id {DebugId}).", authorizationId, ex.PayPalDebugId);
        }
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        object body = amount.HasValue
            ? new PayPalRefundRequest
            {
                Amount = new PayPalMoneyDto { CurrencyCode = currency, Value = FormatMoney(amount.Value) }
            }
            : new { };

        var response = await SendAsync(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken);

        var refund = Deserialize<PayPalRefundResource>(response.Json)
                     ?? throw new PaymentOperationException(502, "PayPal refund returned an empty body.", response.DebugId);

        return new PayPalRefundResult
        {
            RefundId = refund.Id ?? throw new PaymentOperationException(502, "PayPal refund did not return an id.", response.DebugId),
            Status = refund.Status ?? string.Empty,
            Amount = ParseMoney(refund.Amount?.Value),
            Currency = refund.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        string merchantCustomerId,
        CardPaymentDetails card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalVaultRequest
        {
            Customer = new PayPalVaultCustomer { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new PayPalVaultPaymentSource
            {
                Card = BuildCardRequest(card, vaultId: null, storedCredential: null)
            }
        };

        var response = await SendAsync(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            body,
            requestId,
            cancellationToken);

        var vault = Deserialize<PayPalVaultResponse>(response.Json)
                    ?? throw new PaymentOperationException(502, "PayPal vault returned an empty body.", response.DebugId);

        if (string.IsNullOrEmpty(vault.Id))
        {
            throw new PaymentOperationException(502, "PayPal vault did not return a payment token id.", response.DebugId);
        }

        return new PayPalVaultedCard
        {
            PaymentTokenId = vault.Id,
            LastDigits = vault.PaymentSource?.Card?.LastDigits,
            Brand = vault.PaymentSource?.Card?.Brand,
            Expiry = vault.PaymentSource?.Card?.Expiry,
            Name = vault.PaymentSource?.Card?.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(
                HttpMethod.Delete,
                $"v3/vault/payment-tokens/{paymentTokenId}",
                body: null,
                requestId: null,
                cancellationToken,
                allowEmptyBody: true);
        }
        catch (PaymentOperationException ex) when (ex.StatusCode == 404)
        {
            _logger.LogInformation("PayPal payment token {TokenId} was already deleted (debug_id {DebugId}).", paymentTokenId, ex.PayPalDebugId);
        }
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        foreach (var window in SplitDateRange(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                var start = FormatPayPalTime(window.From);
                var end = FormatPayPalTime(window.To);
                var path =
                    $"v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=transaction_info&page_size=500&page={page}&balance_affecting_records_only=N";

                PayPalApiResponse response;
                try
                {
                    response = await SendAsync(HttpMethod.Get, path, body: null, requestId: null, cancellationToken);
                }
                catch (PaymentOperationException ex) when (ex.StatusCode is 400 or 403 or 404)
                {
                    _logger.LogWarning("PayPal transaction search returned {Status} for {From}–{To} (debug_id {DebugId}): {Message}",
                        ex.StatusCode, start, end, ex.PayPalDebugId, ex.Message);
                    break;
                }

                var payload = Deserialize<PayPalTransactionSearchResponse>(response.Json) ?? new PayPalTransactionSearchResponse();
                if (payload.TransactionDetails is not null)
                {
                    foreach (var detail in payload.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null || string.IsNullOrEmpty(info.TransactionId))
                        {
                            continue;
                        }

                        results.Add(new PayPalReportedTransaction
                        {
                            TransactionId = info.TransactionId,
                            ReferenceId = info.PaypalReferenceId,
                            InvoiceId = info.InvoiceId,
                            CustomField = info.CustomField,
                            EventCode = info.TransactionEventCode,
                            Status = info.TransactionStatus,
                            InitiationDate = ParseTime(info.TransactionInitiationDate),
                            AmountValue = info.TransactionAmount?.Value,
                            AmountCurrency = info.TransactionAmount?.CurrencyCode,
                            FeeValue = info.FeeAmount?.Value
                        });
                    }
                }

                totalPages = payload.TotalPages.GetValueOrDefault(1);
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        IReadOnlyList<PayPalPurchaseItem> items,
        PayPalPaymentSourceRequest paymentSource,
        string requestId,
        CancellationToken cancellationToken)
    {
        var currency = Currency;
        var formattedAmount = FormatMoney(amount);
        var invoiceId = InvoiceIdFor(orderId, requestId);
        var purchaseItems = new List<PayPalItemRequest>();
        foreach (var item in items)
        {
            purchaseItems.Add(new PayPalItemRequest
            {
                Name = Truncate(item.Name, 127),
                Quantity = item.Quantity,
                UnitAmount = new PayPalMoneyDto { CurrencyCode = currency, Value = item.UnitAmount.Value },
                Sku = item.Sku,
                Category = item.Category
            });
        }

        var request = new PayPalOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits =
            [
                new PayPalPurchaseUnitRequest
                {
                    ReferenceId = "default",
                    CustomId = invoiceId,
                    InvoiceId = invoiceId,
                    Description = $"eShopOnWeb order {orderId}",
                    Amount = new PayPalAmountRequest
                    {
                        CurrencyCode = currency,
                        Value = formattedAmount,
                        Breakdown = new PayPalAmountBreakdown
                        {
                            ItemTotal = new PayPalMoneyDto { CurrencyCode = currency, Value = formattedAmount }
                        }
                    },
                    Items = purchaseItems
                }
            ],
            PaymentSource = paymentSource
        };

        var createResponse = await SendAsync(
            HttpMethod.Post,
            "v2/checkout/orders",
            request,
            requestId,
            cancellationToken);

        var order = Deserialize<PayPalOrderResponse>(createResponse.Json)
                    ?? throw new PaymentOperationException(502, "PayPal create order returned an empty body.", createResponse.DebugId);

        EnsureNoPayerActionRequired(order, createResponse.DebugId);

        var authorization = ExtractAuthorization(order);
        if (authorization is null && RequiresPayerAction(order))
        {
            throw new PayerActionRequiredException(order.Id ?? "unknown", createResponse.DebugId);
        }

        if (authorization is null && !string.IsNullOrEmpty(order.Id) &&
            !string.Equals(order.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            var authorizeResponse = await SendAsync(
                HttpMethod.Post,
                $"v2/checkout/orders/{order.Id}/authorize",
                new { },
                requestId + "-authorize",
                cancellationToken);

            order = Deserialize<PayPalOrderResponse>(authorizeResponse.Json)
                    ?? throw new PaymentOperationException(502, "PayPal authorize returned an empty body.", authorizeResponse.DebugId);
            EnsureNoPayerActionRequired(order, authorizeResponse.DebugId);
            authorization = ExtractAuthorization(order);
            if (authorization is null && RequiresPayerAction(order))
            {
                throw new PayerActionRequiredException(order.Id ?? "unknown", authorizeResponse.DebugId);
            }
        }

        if (authorization is null)
        {
            throw new PaymentOperationException(502,
                $"PayPal did not return an authorization for order {orderId} (PayPal order {order.Id}, status {order.Status}).",
                createResponse.DebugId);
        }

        var held = ParseMoney(authorization.Amount?.Value);
        var expected = ParseMoney(formattedAmount);
        if (held != expected)
        {
            throw new PaymentOperationException(502,
                $"PayPal held {held} {currency} but the order total is {expected} {currency}.",
                createResponse.DebugId);
        }

        return MapAuthorization(order.Id ?? string.Empty, order.Status ?? string.Empty, authorization, invoiceId);
    }

    private static PayPalCardRequest BuildCardRequest(
        CardPaymentDetails card,
        string? vaultId,
        PayPalStoredCredential? storedCredential)
    {
        return new PayPalCardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            VaultId = vaultId,
            StoredCredential = storedCredential,
            BillingAddress = card.BillingAddress is null
                ? null
                : new PayPalCardBillingAddress
                {
                    CountryCode = card.BillingAddress.CountryCode,
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode
                }
        };
    }

    private void EnsureNoPayerActionRequired(PayPalOrderResponse order, string? debugId)
    {
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(order.Id ?? "unknown", debugId);
        }

        if (RequiresPayerAction(order) && ExtractAuthorization(order) is null)
        {
            throw new PayerActionRequiredException(order.Id ?? "unknown", debugId);
        }
    }

    private static bool RequiresPayerAction(PayPalOrderResponse order)
    {
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (order.Links is null)
        {
            return false;
        }

        foreach (var link in order.Links)
        {
            if (string.Equals(link.Rel, "payer-action", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static PayPalAuthorizationResource? ExtractAuthorization(PayPalOrderResponse order)
    {
        var units = order.PurchaseUnits;
        if (units is null)
        {
            return null;
        }

        foreach (var unit in units)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is { Count: > 0 })
            {
                return authorizations[0];
            }
        }

        return null;
    }

    private PayPalAuthorizationResult MapAuthorization(
        string paypalOrderId,
        string orderStatus,
        PayPalAuthorizationResource resource,
        string? invoiceId = null)
    {
        return new PayPalAuthorizationResult
        {
            PayPalOrderId = paypalOrderId,
            OrderStatus = orderStatus,
            AuthorizationId = resource.Id ?? string.Empty,
            AuthorizationStatus = resource.Status ?? string.Empty,
            ExpirationTime = ParseTime(resource.ExpirationTime),
            CreateTime = ParseTime(resource.CreateTime),
            Amount = resource.Amount is null
                ? null
                : new PayPalMoney { CurrencyCode = resource.Amount.CurrencyCode ?? Currency, Value = resource.Amount.Value ?? string.Empty },
            InvoiceId = invoiceId
        };
    }

    private async Task<PayPalApiResponse> SendAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowEmptyBody = false)
    {
        EnsureCredentials();

        const int maxAttempts = 4;
        PaymentOperationException? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var token = await GetAccessTokenAsync(forceRefresh: attempt > 1 && lastError?.StatusCode == 401, cancellationToken);
            using var request = new HttpRequestMessage(method, relativePath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrEmpty(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, PayPalJson.Options);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = new PaymentOperationException(504, $"PayPal request to {method} {relativePath} timed out.");
                await DelayBackoff(attempt, cancellationToken);
                continue;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var debugId = TryReadDebugId(payload);

            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(payload) && !allowEmptyBody && method != HttpMethod.Delete)
                {
                    // 204 void is success with no body.
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        return new PayPalApiResponse("{}", debugId);
                    }
                }

                _logger.LogInformation("PayPal {Method} {Path} succeeded with {Status} (debug_id {DebugId}).",
                    method, SanitizePath(relativePath), (int)response.StatusCode, debugId);
                return new PayPalApiResponse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload, debugId);
            }

            var error = TryDeserializeError(payload);
            var status = (int)response.StatusCode;
            var message = FormatPayPalError(error, status, relativePath);

            if (status == 401 && attempt < maxAttempts)
            {
                _cache.Remove(TokenCacheKey);
                lastError = new PaymentOperationException(status, message, error?.DebugId ?? debugId);
                continue;
            }

            if ((status == 429 || status >= 500) && attempt < maxAttempts)
            {
                _logger.LogWarning("PayPal {Method} {Path} returned {Status} (debug_id {DebugId}); retrying.",
                    method, SanitizePath(relativePath), status, error?.DebugId ?? debugId);
                lastError = new PaymentOperationException(status, message, error?.DebugId ?? debugId);
                await DelayBackoff(attempt, cancellationToken);
                continue;
            }

            _logger.LogWarning("PayPal {Method} {Path} failed with {Status} {Name} (debug_id {DebugId}).",
                method, SanitizePath(relativePath), status, error?.Name, error?.DebugId ?? debugId);
            if (error?.Details is { Count: > 0 })
            {
                foreach (var detail in error.Details)
                {
                    var safeDescription = ContainsDigitsThatLookLikeACard(detail.Description ?? string.Empty)
                        ? "(redacted)"
                        : detail.Description;
                    _logger.LogWarning("PayPal error detail issue={Issue} field={Field} description={Description}",
                        detail.Issue, detail.Field, safeDescription);
                }
            }

            throw new PaymentOperationException(status >= 400 && status < 600 ? status : 502, message, error?.DebugId ?? debugId);
        }

        throw lastError ?? new PaymentOperationException(502, "PayPal request failed.");
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        EnsureCredentials();

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials"
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = TryDeserializeError(payload);
            throw new PaymentOperationException(
                (int)response.StatusCode,
                FormatPayPalError(error, (int)response.StatusCode, "v1/oauth2/token"),
                error?.DebugId);
        }

        var token = JsonSerializer.Deserialize<PayPalTokenResponse>(payload, PayPalJson.Options)
                    ?? throw new PaymentOperationException(502, "PayPal token response was empty.");
        if (string.IsNullOrEmpty(token.AccessToken))
        {
            throw new PaymentOperationException(502, "PayPal token response did not include an access_token.");
        }

        var lifetime = token.ExpiresIn > 60 ? TimeSpan.FromSeconds(token.ExpiresIn - 60) : TimeSpan.FromSeconds(Math.Max(token.ExpiresIn, 1));
        _cache.Set(TokenCacheKey, token.AccessToken, lifetime);
        return token.AccessToken;
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new PaymentOperationException(500,
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret (from PAYPAL_CLIENT_ID and PAYPAL_CLIENT_SECRET).");
        }
    }

    private static string ResolveBaseUrl(PayPalSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.BaseUrl.Trim().TrimEnd('/');
        }

        var environment = settings.Environment?.Trim();
        if (string.Equals(environment, "live", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-m.paypal.com";
        }

        return "https://api-m.sandbox.paypal.com";
    }

    private static string InvoiceIdFor(int orderId, string requestId)
    {
        var suffix = requestId.Length >= 12 ? requestId[..12] : requestId;
        return $"ESHOP-{orderId}-{suffix}";
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static string SanitizePath(string path)
        => path.Contains('?') ? path.Split('?')[0] : path;

    private static T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, PayPalJson.Options);
    }

    private static PayPalErrorBody? TryDeserializeError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<PayPalErrorBody>(payload, PayPalJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadDebugId(string payload)
    {
        var error = TryDeserializeError(payload);
        return error?.DebugId;
    }

    private static string FormatPayPalError(PayPalErrorBody? error, int status, string path)
    {
        var name = error?.Name ?? "PAYPAL_ERROR";
        var message = error?.Message ?? "PayPal request failed.";
        var issue = error?.Details is { Count: > 0 } ? error.Details[0].Issue : null;
        var description = error?.Details is { Count: > 0 } ? error.Details[0].Description : null;
        var field = error?.Details is { Count: > 0 } ? error.Details[0].Field : null;
        var parts = new List<string> { $"{name}: {message}" };
        if (!string.IsNullOrEmpty(issue))
        {
            parts.Add(issue);
        }

        if (!string.IsNullOrEmpty(field) && !ContainsDigitsThatLookLikeACard(field))
        {
            parts.Add($"field {field}");
        }

        if (!string.IsNullOrEmpty(description) && !ContainsDigitsThatLookLikeACard(description))
        {
            parts.Add(description);
        }

        parts.Add($"HTTP {status} {SanitizePath(path)}");
        if (!string.IsNullOrEmpty(error?.DebugId))
        {
            parts.Add($"debug_id {error.DebugId}");
        }

        return string.Join(" — ", parts);
    }

    private static bool ContainsDigitsThatLookLikeACard(string value)
    {
        var digits = 0;
        foreach (var ch in value)
        {
            if (char.IsDigit(ch))
            {
                digits++;
                if (digits >= 13)
                {
                    return true;
                }
            }
            else
            {
                digits = 0;
            }
        }

        return false;
    }

    private static bool IsAlreadyVoided(PaymentOperationException ex)
        => ex.Message.Contains("VOIDED", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthorizationUnrenewable(PaymentOperationException ex)
        => ex.Message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("MAX_NUMBER_OF_REAUTHORIZATION_ALLOWED", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("CANNOT_BE_REAUTHORIZED", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase);

    internal static bool IsStaleAuthorizationIssue(PaymentOperationException ex)
        => ex.Message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("EXPIRED_AUTHORIZATION", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
           || ex.Message.Contains("DEC", StringComparison.OrdinalIgnoreCase) && ex.Message.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatPayPalTime(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> SplitDateRange(DateTimeOffset from, DateTimeOffset to)
    {
        var window = TimeSpan.FromDays(31).Subtract(TimeSpan.FromSeconds(1));
        var cursor = from;
        while (cursor < to)
        {
            var end = cursor + window;
            if (end > to)
            {
                end = to;
            }

            yield return (cursor, end);
            cursor = end.AddSeconds(1);
        }
    }

    private static async Task DelayBackoff(int attempt, CancellationToken cancellationToken)
    {
        var delayMs = (int)(Math.Pow(2, attempt) * 250);
        delayMs += Random.Shared.Next(0, 250);
        await Task.Delay(delayMs, cancellationToken);
    }

    private readonly record struct PayPalApiResponse(string Json, string? DebugId);
}
