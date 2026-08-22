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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Regex CardNumberJson = new("\"number\"\\s*:\\s*\"[^\"]+\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SecurityCodeJson = new("\"security_code\"\\s*:\\s*\"[^\"]+\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        decimal amount,
        string currency,
        string customId,
        string invoiceId,
        IReadOnlyList<PayPalPurchaseLine> items,
        CardPaymentSource card,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSourceDto
        {
            Card = new PayPalCardRequestDto
            {
                Name = card.Name,
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                BillingAddress = MapBillingAddress(card.BillingAddress)
            }
        };

        return AuthorizeInternalAsync(amount, currency, customId, invoiceId, items, paymentSource, idempotencyKey, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        decimal amount,
        string currency,
        string customId,
        string invoiceId,
        IReadOnlyList<PayPalPurchaseLine> items,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSourceDto
        {
            Card = new PayPalCardRequestDto
            {
                VaultId = vaultId,
                StoredCredential = new PayPalStoredCredentialDto
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "UNSCHEDULED",
                    Usage = "SUBSEQUENT"
                }
            }
        };

        return AuthorizeInternalAsync(amount, currency, customId, invoiceId, items, paymentSource, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var resource = await SendAsync<PayPalAuthorizationResourceDto>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        return MapAuthorizationDetails(resource);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalReauthorizeRequestDto
        {
            Amount = Money(amount, currency)
        };

        var resource = await SendAsync<PayPalAuthorizationResourceDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            idempotencyKey,
            cancellationToken);

        return MapAuthorizationDetails(resource);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCaptureRequestDto
        {
            Amount = Money(amount, currency),
            FinalCapture = true,
            InvoiceId = invoiceId
        };

        var resource = await SendAsync<PayPalCaptureResourceDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body,
            idempotencyKey,
            cancellationToken);

        return MapCapture(resource, currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorizationResourceDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            body: new { },
            requestId: idempotencyKey,
            cancellationToken,
            allowEmpty: true);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalRefundRequestDto
        {
            Amount = Money(amount, currency)
        };

        var resource = await SendAsync<PayPalRefundResourceDto>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            idempotencyKey,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(resource.Id))
        {
            throw new PaymentException("PayPal refund succeeded but returned no refund id.", 502);
        }

        return new PayPalRefundResult
        {
            RefundId = resource.Id,
            Status = resource.Status ?? "COMPLETED",
            Amount = PayPalMoneyFormat.Parse(resource.Amount?.Value),
            Currency = resource.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        CardPaymentSource card,
        string merchantCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCreatePaymentTokenRequestDto
        {
            PaymentSource = new PayPalVaultPaymentSourceDto
            {
                Card = new PayPalVaultCardDto
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = MapBillingAddress(card.BillingAddress)
                }
            },
            Customer = new PayPalVaultCustomerDto
            {
                MerchantCustomerId = merchantCustomerId
            }
        };

        var resource = await SendAsync<PayPalPaymentTokenResponseDto>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            body,
            idempotencyKey,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(resource.Id))
        {
            throw new PaymentException("PayPal vaulted the card but returned no payment token id.", 502);
        }

        var lastDigits = resource.PaymentSource?.Card?.LastDigits;
        if (string.IsNullOrWhiteSpace(lastDigits))
        {
            lastDigits = card.LastDigits;
        }

        return new PayPalVaultedCard
        {
            PaymentTokenId = resource.Id,
            CustomerId = resource.Customer?.Id,
            LastDigits = lastDigits,
            Brand = resource.PaymentSource?.Card?.Brand,
            Expiry = resource.PaymentSource?.Card?.Expiry,
            CardholderName = resource.PaymentSource?.Card?.Name ?? card.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<object>(
                HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{paymentTokenId}",
                body: null,
                requestId: null,
                cancellationToken,
                allowEmpty: true);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("PayPal payment token {TokenId} was already deleted.", paymentTokenId);
        }
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from.ToUniversalTime();
        var end = to.ToUniversalTime();
        var maxWindow = TimeSpan.FromDays(31);

        while (windowStart < end)
        {
            var windowEnd = windowStart + maxWindow;
            if (windowEnd > end)
            {
                windowEnd = end;
            }

            var page = 1;
            int? totalPages = null;
            while (true)
            {
                var startDate = FormatSearchTimestamp(windowStart);
                var endDate = FormatSearchTimestamp(windowEnd);
                var path =
                    $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(startDate)}&end_date={Uri.EscapeDataString(endDate)}" +
                    $"&page={page}&page_size=500&fields=all&balance_affecting_records_only=N";

                var response = await SendAsync<PayPalTransactionSearchResponseDto>(
                    HttpMethod.Get,
                    path,
                    body: null,
                    requestId: null,
                    cancellationToken);

                var details = response.TransactionDetails ?? new List<PayPalTransactionDetailDto>();
                foreach (var detail in details)
                {
                    var info = detail.TransactionInfo;
                    if (info is null || string.IsNullOrWhiteSpace(info.TransactionId))
                    {
                        continue;
                    }

                    DateTimeOffset? initiation = null;
                    if (!string.IsNullOrWhiteSpace(info.TransactionInitiationDate) &&
                        DateTimeOffset.TryParse(info.TransactionInitiationDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                    {
                        initiation = parsed;
                    }

                    results.Add(new PayPalReportedTransaction
                    {
                        TransactionId = info.TransactionId,
                        ReferenceId = info.PaypalReferenceId,
                        InvoiceId = info.InvoiceId,
                        CustomField = info.CustomField,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        Amount = PayPalMoneyFormat.Parse(info.TransactionAmount?.Value),
                        Currency = info.TransactionAmount?.CurrencyCode,
                        InitiationDate = initiation
                    });
                }

                if (response.TotalPages > 0)
                {
                    totalPages = response.TotalPages;
                }

                if (details.Count == 0)
                {
                    break;
                }

                if (totalPages.HasValue)
                {
                    if (page >= totalPages.Value)
                    {
                        break;
                    }
                }
                else if (details.Count < 500)
                {
                    break;
                }

                page++;
            }

            windowStart = windowEnd;
        }

        return results;
    }

    private async Task<PayPalAuthorizationResult> AuthorizeInternalAsync(
        decimal amount,
        string currency,
        string customId,
        string invoiceId,
        IReadOnlyList<PayPalPurchaseLine> items,
        PayPalPaymentSourceDto paymentSource,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var amountValue = PayPalMoneyFormat.Format(amount, currency);
        var createRequest = new PayPalCreateOrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequestDto>
            {
                new()
                {
                    CustomId = customId,
                    InvoiceId = invoiceId,
                    Amount = new PayPalAmountRequestDto
                    {
                        CurrencyCode = currency,
                        Value = amountValue,
                        Breakdown = new PayPalAmountBreakdownDto
                        {
                            ItemTotal = Money(amount, currency)
                        }
                    },
                    Items = items.Select(i => new PayPalItemDto
                    {
                        Name = Truncate(i.Name, 127),
                        Quantity = i.Quantity.ToString(CultureInfo.InvariantCulture),
                        UnitAmount = Money(i.UnitAmount, currency)
                    }).ToList()
                }
            }
        };

        var created = await SendAsync<PayPalOrderResourceDto>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            createRequest,
            $"{idempotencyKey}-create",
            cancellationToken);

        EnsureNoPayerAction(created);

        if (string.IsNullOrWhiteSpace(created.Id))
        {
            throw new PaymentException("PayPal created an order but returned no id.", 502);
        }

        var authorized = await SendAsync<PayPalOrderResourceDto>(
            HttpMethod.Post,
            $"/v2/checkout/orders/{created.Id}/authorize",
            new PayPalAuthorizeRequestDto { PaymentSource = paymentSource },
            $"{idempotencyKey}-authorize",
            cancellationToken);

        EnsureNoPayerAction(authorized);

        var authorization = authorized.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorizationResourceDto>())
            .FirstOrDefault();

        if (authorization is null || string.IsNullOrWhiteSpace(authorization.Id))
        {
            throw new PaymentException("PayPal authorized the order but returned no authorization id.", 502);
        }

        DateTimeOffset? expiration = null;
        if (!string.IsNullOrWhiteSpace(authorization.ExpirationTime) &&
            DateTimeOffset.TryParse(authorization.ExpirationTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedExpiration))
        {
            expiration = parsedExpiration;
        }

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = authorized.Id ?? created.Id,
            AuthorizationId = authorization.Id,
            AuthorizationStatus = authorization.Status ?? "CREATED",
            Amount = PayPalMoneyFormat.Parse(authorization.Amount?.Value),
            Currency = authorization.Amount?.CurrencyCode ?? currency,
            Expiration = expiration
        };
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowEmpty = false) where T : class
    {
        _options.EnsureConfigured();
        var url = _options.ResolveBaseUrl() + path;
        Exception? lastException = null;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt) + Random.Shared.Next(50, 250));
                await Task.Delay(delay, cancellationToken);
            }

            using var request = new HttpRequestMessage(method, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (attempt < 3)
            {
                lastException = ex;
                _logger.LogWarning(ex, "PayPal {Method} {Path} failed to send (attempt {Attempt}).", method, RedactPath(path), attempt + 1);
                continue;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                InvalidateToken();
                continue;
            }

            if ((int)response.StatusCode == 429)
            {
                _logger.LogWarning("PayPal rate limited {Method} {Path}. Retrying.", method, RedactPath(path));
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                if (allowEmpty && string.IsNullOrWhiteSpace(payload))
                {
                    return default!;
                }

                if (string.IsNullOrWhiteSpace(payload))
                {
                    if (allowEmpty)
                    {
                        return default!;
                    }

                    throw new PaymentException("PayPal returned an empty success response.", 502);
                }

                var parsed = JsonSerializer.Deserialize<T>(payload, JsonOptions);
                if (parsed is null)
                {
                    throw new PaymentException("PayPal returned a response that could not be read.", 502);
                }

                return parsed;
            }

            var error = TryParseError(payload);
            var debugId = error?.DebugId;
            if (!string.IsNullOrWhiteSpace(debugId))
            {
                _logger.LogWarning(
                    "PayPal {Method} {Path} failed with {Status} name={Name} debug_id={DebugId} issue={Issue} field={Field} description={Description}",
                    method,
                    RedactPath(path),
                    (int)response.StatusCode,
                    error?.Name,
                    debugId,
                    error?.Details?.FirstOrDefault()?.Issue,
                    error?.Details?.FirstOrDefault()?.Field,
                    error?.Details?.FirstOrDefault()?.Description);
            }

            if ((int)response.StatusCode >= 500 && attempt < 3 && !string.IsNullOrWhiteSpace(requestId))
            {
                continue;
            }

            ThrowMappedError(response.StatusCode, error, payload);
        }

        throw lastException ?? new PaymentException("PayPal request failed after retries.", 502);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            _options.EnsureConfigured();
            var tokenUrl = _options.ResolveBaseUrl() + "/v1/oauth2/token";
            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = TryParseError(payload);
                _logger.LogWarning("PayPal token request failed with {Status} debug_id={DebugId}", (int)response.StatusCode, error?.DebugId);
                throw new PaymentException("Unable to authenticate with PayPal. Check PayPal:ClientId and PayPal:ClientSecret.", 502);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponseDto>(payload, JsonOptions);
            if (token?.AccessToken is null)
            {
                throw new PaymentException("PayPal token response did not include an access_token.", 502);
            }

            _accessToken = token.AccessToken;
            var lifetime = token.ExpiresIn > 60 ? token.ExpiresIn - 60 : token.ExpiresIn;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(lifetime, 30));
            return _accessToken;
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

    private static void EnsureNoPayerAction(PayPalOrderResourceDto order)
    {
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            (order.Links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false))
        {
            throw new PayerActionRequiredException(order.Id, debugId: null);
        }
    }

    private static PayPalCaptureResult MapCapture(PayPalCaptureResourceDto resource, string fallbackCurrency)
    {
        if (string.IsNullOrWhiteSpace(resource.Id))
        {
            throw new PaymentException("PayPal capture succeeded but returned no capture id.", 502);
        }

        var captured = PayPalMoneyFormat.Parse(resource.SellerReceivableBreakdown?.GrossAmount?.Value ?? resource.Amount?.Value);
        var fee = PayPalMoneyFormat.Parse(resource.SellerReceivableBreakdown?.PaypalFee?.Value);
        var net = resource.SellerReceivableBreakdown?.NetAmount is not null
            ? PayPalMoneyFormat.Parse(resource.SellerReceivableBreakdown.NetAmount.Value)
            : captured - fee;

        return new PayPalCaptureResult
        {
            CaptureId = resource.Id,
            Status = resource.Status ?? "COMPLETED",
            CapturedAmount = captured,
            PaypalFee = fee,
            NetProceeds = net,
            Currency = resource.Amount?.CurrencyCode ?? fallbackCurrency
        };
    }

    private static PayPalAuthorizationDetails MapAuthorizationDetails(PayPalAuthorizationResourceDto resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Id))
        {
            throw new PaymentException("PayPal authorization details were missing an id.", 502);
        }

        DateTimeOffset? expiration = null;
        if (!string.IsNullOrWhiteSpace(resource.ExpirationTime) &&
            DateTimeOffset.TryParse(resource.ExpirationTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedExpiration))
        {
            expiration = parsedExpiration;
        }

        DateTimeOffset? created = null;
        if (!string.IsNullOrWhiteSpace(resource.CreateTime) &&
            DateTimeOffset.TryParse(resource.CreateTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedCreated))
        {
            created = parsedCreated;
        }

        return new PayPalAuthorizationDetails
        {
            AuthorizationId = resource.Id,
            Status = resource.Status ?? string.Empty,
            Amount = PayPalMoneyFormat.Parse(resource.Amount?.Value),
            Currency = resource.Amount?.CurrencyCode ?? string.Empty,
            Expiration = expiration,
            CreateTime = created
        };
    }

    private static PayPalMoneyDto Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = PayPalMoneyFormat.Format(amount, currency)
    };

    private static PayPalBillingAddressDto? MapBillingAddress(CardBillingAddress? address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.CountryCode))
        {
            return null;
        }

        return new PayPalBillingAddressDto
        {
            CountryCode = NormalizeCountryCode(address.CountryCode),
            AddressLine1 = NullIfBlank(address.AddressLine1),
            AddressLine2 = NullIfBlank(address.AddressLine2),
            AdminArea2 = NullIfBlank(address.AdminArea2),
            AdminArea1 = NullIfBlank(address.AdminArea1),
            PostalCode = NullIfBlank(address.PostalCode)
        };
    }

    private static string NormalizeCountryCode(string countryCode)
    {
        var trimmed = countryCode.Trim().ToUpperInvariant();
        return trimmed switch
        {
            "USA" => "US",
            "GBR" => "GB",
            "CAN" => "CA",
            _ => trimmed
        };
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PayPalErrorResponseDto? TryParseError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PayPalErrorResponseDto>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ThrowMappedError(HttpStatusCode statusCode, PayPalErrorResponseDto? error, string payload)
    {
        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var field = error?.Details?.FirstOrDefault()?.Field;
        var description = error?.Details?.FirstOrDefault()?.Description;
        var message = description
                      ?? error?.Message
                      ?? "PayPal rejected the request.";

        if (!string.IsNullOrWhiteSpace(field))
        {
            message = $"{message} Field: {field}.";
        }

        if (!string.IsNullOrWhiteSpace(error?.DebugId))
        {
            message = $"{message} (debug_id {error.DebugId})";
        }

        if (string.Equals(issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(error?.Name, "UNPROCESSABLE_ENTITY", StringComparison.OrdinalIgnoreCase)
            && payload.Contains("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(paypalOrderId: null, error?.DebugId);
        }

        var status = (int)statusCode;
        if (status == 404)
        {
            throw new PaymentException(message, 404);
        }

        if (status is 400 or 409 or 422)
        {
            throw new PaymentException($"{message}{(issue is null ? string.Empty : $" ({issue})")}", status == 422 ? 409 : status);
        }

        throw new PaymentException(message, 502);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string RedactPath(string path) => path;

    internal static string RedactBody(string json)
    {
        var redacted = CardNumberJson.Replace(json, "\"number\":\"[redacted]\"");
        return SecurityCodeJson.Replace(redacted, "\"security_code\":\"[redacted]\"");
    }

    private static string FormatSearchTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
