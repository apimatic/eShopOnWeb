using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Hand-written PayPal REST client built against the OpenAPI specifications in
/// api-specs/paypal. All paths, field names, the OAuth2 client-credentials auth scheme
/// (tokenUrl /v1/oauth2/token) and the error model come from those specs.
/// Full card numbers pass through this client to PayPal only; they are never logged
/// and never persisted.
/// </summary>
public class PayPalClient : IPayPalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(HttpClient httpClient, PayPalSettings settings, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<PayPalOrderInfo> CreateOrderAsync(decimal amount, string currency, string referenceId, string invoiceId, PayPalPaymentSource paymentSource, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new CreateOrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequestDto>
            {
                new PurchaseUnitRequestDto
                {
                    ReferenceId = referenceId,
                    InvoiceId = invoiceId,
                    CustomId = referenceId,
                    Amount = new MoneyDto { CurrencyCode = currency, Value = FormatMoney(amount) }
                }
            },
            PaymentSource = BuildPaymentSource(paymentSource)
        };

        var order = await SendAsync<OrderDto>(HttpMethod.Post, "/v2/checkout/orders", request, requestId, cancellationToken);
        if (order?.Id == null)
        {
            throw new PayPalApiException(500, null, null, null, "PayPal did not return an order id.");
        }

        var authorization = order.PurchaseUnits?.SelectMany(p => p.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationDto>()).FirstOrDefault();
        return new PayPalOrderInfo
        {
            Id = order.Id,
            Status = order.Status ?? string.Empty,
            Authorization = authorization?.Id != null ? MapAuthorization(authorization) : null
        };
    }

    public async Task<PayPalAuthorizationInfo> AuthorizeOrderAsync(string payPalOrderId, string requestId, CancellationToken cancellationToken = default)
    {
        var order = await SendAsync<OrderDto>(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/authorize",
            new { }, requestId, cancellationToken);

        var authorization = order?.PurchaseUnits?.SelectMany(p => p.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationDto>()).FirstOrDefault();
        if (authorization?.Id == null)
        {
            throw new PayPalApiException(500, null, null, null, "PayPal did not return an authorization for the order.");
        }

        return MapAuthorization(authorization);
    }

    public async Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<AuthorizationDto>(HttpMethod.Get, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            null, null, cancellationToken);
        return MapAuthorization(authorization!);
    }

    public async Task<PayPalCaptureInfo> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string? invoiceId, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new CaptureRequestDto
        {
            Amount = new MoneyDto { CurrencyCode = currency, Value = FormatMoney(amount) },
            InvoiceId = invoiceId,
            FinalCapture = true
        };

        var capture = await SendAsync<CaptureDto>(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            request, requestId, cancellationToken);

        return new PayPalCaptureInfo
        {
            Id = capture!.Id!,
            Status = capture.Status ?? string.Empty,
            Amount = ParseMoney(capture.Amount),
            Currency = capture.Amount?.CurrencyCode ?? currency,
            PayPalFee = ParseNullableMoney(capture.SellerReceivableBreakdown?.PaypalFee),
            NetAmount = ParseNullableMoney(capture.SellerReceivableBreakdown?.NetAmount),
            FinalCapture = capture.FinalCapture ?? true
        };
    }

    public async Task<PayPalAuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new ReauthorizeRequestDto
        {
            Amount = new MoneyDto { CurrencyCode = currency, Value = FormatMoney(amount) }
        };

        var authorization = await SendAsync<AuthorizationDto>(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            request, requestId, cancellationToken);
        return MapAuthorization(authorization!);
    }

    public async Task<PayPalAuthorizationInfo> VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<AuthorizationDto>(HttpMethod.Post, $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            new { }, requestId, cancellationToken);
        return MapAuthorization(authorization!);
    }

    public async Task<PayPalRefundInfo> RefundCaptureAsync(string captureId, decimal? amount, string currency, string? customId, string? noteToPayer, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new RefundRequestDto
        {
            Amount = amount.HasValue ? new MoneyDto { CurrencyCode = currency, Value = FormatMoney(amount.Value) } : null,
            CustomId = customId,
            NoteToPayer = noteToPayer
        };

        var refund = await SendAsync<RefundDto>(HttpMethod.Post, $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            request, requestId, cancellationToken);

        return new PayPalRefundInfo
        {
            Id = refund!.Id!,
            Status = refund.Status ?? string.Empty,
            Amount = ParseMoney(refund.Amount),
            Currency = refund.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCard> CreateVaultPaymentTokenAsync(CardDetails card, string merchantCustomerId, string requestId, CancellationToken cancellationToken = default)
    {
        var request = new VaultTokenRequestDto
        {
            PaymentSource = new VaultPaymentSourceDto
            {
                Card = new VaultCardDto
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            },
            Customer = new VaultCustomerDto { MerchantCustomerId = merchantCustomerId }
        };

        var token = await SendAsync<VaultTokenResponseDto>(HttpMethod.Post, "/v3/vault/payment-tokens", request, requestId, cancellationToken);

        return new PayPalVaultedCard
        {
            VaultTokenId = token!.Id!,
            Brand = token.PaymentSource?.Card?.Brand,
            LastDigits = token.PaymentSource?.Card?.LastDigits,
            Expiry = token.PaymentSource?.Card?.Expiry,
            CardholderName = token.PaymentSource?.Card?.Name
        };
    }

    public async Task DeleteVaultPaymentTokenAsync(string vaultTokenId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Delete, $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultTokenId)}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransactionInfo>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransactionInfo>();
        const int pageSize = 100;
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages)
        {
            var path = "/v1/reporting/transactions" +
                $"?start_date={Uri.EscapeDataString(FormatTimestamp(from))}" +
                $"&end_date={Uri.EscapeDataString(FormatTimestamp(to))}" +
                $"&fields=transaction_info" +
                $"&page_size={pageSize}&page={page}";

            var response = await SendAsync<TransactionSearchResponseDto>(HttpMethod.Get, path, null, null, cancellationToken);

            if (response?.TransactionDetails != null)
            {
                results.AddRange(response.TransactionDetails.Select(MapTransaction));
            }

            totalPages = response?.TotalPages ?? 1;
            page++;
        }

        return results;
    }

    private static PayPalTransactionInfo MapTransaction(TransactionDetailDto detail)
    {
        var info = detail.TransactionInfo;
        return new PayPalTransactionInfo
        {
            TransactionId = info?.TransactionId,
            ReferenceId = info?.PaypalReferenceId,
            ReferenceIdType = info?.PaypalReferenceIdType,
            EventCode = info?.TransactionEventCode,
            Status = info?.TransactionStatus,
            Amount = info?.TransactionAmount != null ? ParseNullableMoney(info.TransactionAmount) : null,
            Currency = info?.TransactionAmount?.CurrencyCode,
            Fee = info?.FeeAmount != null ? ParseNullableMoney(info.FeeAmount) : null,
            InitiationTime = ParseTimestamp(info?.TransactionInitiationDate),
            UpdatedTime = ParseTimestamp(info?.TransactionUpdatedDate),
            InvoiceId = info?.InvoiceId,
            CustomField = info?.CustomField
        };
    }

    private static PaymentSourceRequestDto BuildPaymentSource(PayPalPaymentSource source)
    {
        if (!string.IsNullOrEmpty(source.VaultTokenId))
        {
            return new PaymentSourceRequestDto
            {
                Card = new CardRequestDto
                {
                    VaultId = source.VaultTokenId,
                    StoredCredential = new StoredCredentialDto
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "UNSCHEDULED",
                        Usage = "SUBSEQUENT"
                    }
                }
            };
        }

        var card = source.Card ?? throw new ArgumentException("A payment source needs card details or a vault token.", nameof(source));
        return new PaymentSourceRequestDto
        {
            Card = new CardRequestDto
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.Name,
                BillingAddress = MapAddress(card.BillingAddress)
            }
        };
    }

    private static AddressDto? MapAddress(CardBillingAddress? address)
    {
        if (address == null)
        {
            return null;
        }

        return new AddressDto
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static PayPalAuthorizationInfo MapAuthorization(AuthorizationDto authorization)
    {
        return new PayPalAuthorizationInfo
        {
            Id = authorization.Id ?? string.Empty,
            Status = authorization.Status ?? string.Empty,
            Amount = ParseMoney(authorization.Amount),
            Currency = authorization.Amount?.CurrencyCode ?? string.Empty,
            ExpirationTime = ParseTimestamp(authorization.ExpirationTime)
        };
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken, bool isRetry = false)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        if (method == HttpMethod.Post)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }
        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = response.Content != null ? await response.Content.ReadAsStringAsync(cancellationToken) : string.Empty;

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !isRetry)
        {
            _accessToken = null;
            return await SendAsync<T>(method, path, body, requestId, cancellationToken, isRetry: true);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw ParseError((int)response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content) || typeof(T) == typeof(object))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions);
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

            if (string.IsNullOrEmpty(_settings.ClientId) || string.IsNullOrEmpty(_settings.ClientSecret))
            {
                throw new InvalidOperationException(
                    "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                    "(from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables) via user-secrets or configuration.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw ParseError((int)response.StatusCode, content);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(content, JsonOptions);
            _accessToken = token?.AccessToken ?? throw new PayPalApiException(500, null, null, null, "PayPal did not return an access token.");
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds((token!.ExpiresIn > 120 ? token.ExpiresIn - 120 : 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private PayPalApiException ParseError(int statusCode, string content)
    {
        try
        {
            var error = JsonSerializer.Deserialize<PayPalErrorDto>(content, JsonOptions);
            if (error != null && (error.Name != null || error.Message != null))
            {
                var issue = error.Details?.FirstOrDefault()?.Issue;
                var description = error.Details?.FirstOrDefault()?.Description;
                _logger.LogWarning("PayPal API error {StatusCode} {Name} issue {Issue} debug {DebugId}.",
                    statusCode, error.Name, issue, error.DebugId);
                return new PayPalApiException(statusCode, error.Name, issue, error.DebugId,
                    $"{error.Message}{(description != null ? $" ({issue}: {description})" : string.Empty)}");
            }
        }
        catch (JsonException)
        {
            // fall through to generic error below
        }

        _logger.LogWarning("PayPal API returned HTTP {StatusCode}.", statusCode);
        return new PayPalApiException(statusCode, null, null, null, $"PayPal API returned HTTP {statusCode}.");
    }

    private static string FormatMoney(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(MoneyDto? money) =>
        money?.Value != null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;

    private static decimal? ParseNullableMoney(MoneyDto? money) =>
        money?.Value != null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        value != null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
