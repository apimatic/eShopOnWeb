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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public class PayPalClient : IPayPalClient
{
    private static readonly TimeSpan TokenSkew = TimeSpan.FromSeconds(60);

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options, ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress ??= new Uri(_options.ResolveBaseUrl() + "/");
    }

    public string Currency
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.Currency) || _options.Currency.Length != 3)
            {
                throw new OrderPaymentException(500, "PayPal:Currency is not configured.");
            }

            return _options.Currency.ToUpperInvariant();
        }
    }

    public async Task<PayPalAuthorizationResult> AuthorizeOrderAsync(
        PayPalAuthorizeRequest request,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalOrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequestDto>
            {
                new()
                {
                    Amount = Money(request.Amount, request.Currency),
                    InvoiceId = request.InvoiceId,
                    CustomId = request.CustomId
                }
            },
            PaymentSource = BuildPaymentSource(request)
        };

        var created = await SendAsync<PayPalOrderDto>(
            HttpMethod.Post,
            "v2/checkout/orders",
            body,
            $"{requestId}-create",
            cancellationToken);

        if (created is null || string.IsNullOrWhiteSpace(created.Id))
        {
            throw new OrderPaymentException(502, "PayPal did not return an order id.");
        }

        EnsureNoPayerActionRequired(created);

        var authorization = FirstAuthorization(created);
        if (authorization is null && NeedsAuthorizeCall(created.Status))
        {
            created = await SendAsync<PayPalOrderDto>(
                HttpMethod.Post,
                $"v2/checkout/orders/{created.Id}/authorize",
                new { },
                $"{requestId}-authorize",
                cancellationToken) ?? created;

            EnsureNoPayerActionRequired(created);
            authorization = FirstAuthorization(created);
        }

        if (authorization is null || string.IsNullOrWhiteSpace(authorization.Id))
        {
            throw new OrderPaymentException(502,
                $"PayPal order {created.Id} did not produce an authorization (status {created.Status}).");
        }

        var card = created.PaymentSource?.Card;
        return new PayPalAuthorizationResult
        {
            PayPalOrderId = created.Id!,
            OrderStatus = created.Status ?? string.Empty,
            AuthorizationId = authorization.Id!,
            AuthorizationStatus = authorization.Status ?? string.Empty,
            Expiration = ParseTime(authorization.ExpirationTime),
            AuthorizedAt = ParseTime(authorization.CreateTime) ?? DateTimeOffset.UtcNow,
            Last4 = card?.LastDigits,
            Brand = card?.Brand
        };
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        return ToAuthorizationDetails(dto, authorizationId);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            new PayPalReauthorizeRequestDto { Amount = Money(amount, currency) },
            requestId,
            cancellationToken);

        return ToAuthorizationDetails(dto, authorizationId);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<PayPalAuthorizationDto>(
                HttpMethod.Post,
                $"v2/payments/authorizations/{authorizationId}/void",
                body: null,
                requestId,
                cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode is 404 or 409 || ex.HasIssue("AUTHORIZATION_VOIDED"))
        {
            _logger.LogInformation("PayPal authorization {AuthorizationId} was already voided or missing.", authorizationId);
        }
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            new PayPalCaptureRequestDto
            {
                Amount = Money(amount, currency),
                FinalCapture = true
            },
            requestId,
            cancellationToken);

        return ToCaptureResult(dto);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Get,
            $"v2/payments/captures/{captureId}",
            body: null,
            requestId: null,
            cancellationToken);

        return ToCaptureResult(dto);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        object body = amount.HasValue
            ? new PayPalRefundRequestDto { Amount = Money(amount.Value, currency) }
            : new { };

        var dto = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken);

        if (dto is null || string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new OrderPaymentException(502, "PayPal did not return a refund id.");
        }

        return new PayPalRefundResult
        {
            RefundId = dto.Id!,
            Status = dto.Status ?? string.Empty,
            Amount = amount ?? PayPalMoney.FromValue(dto.Amount?.Value),
            Currency = dto.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCard> CreatePaymentTokenAsync(
        string customerId,
        CardPaymentDetails card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalVaultRequestDto
        {
            Customer = new PayPalVaultCustomerDto { Id = customerId },
            PaymentSource = new PayPalVaultPaymentSourceDto
            {
                Card = new PayPalVaultCardDto
                {
                    Name = card.Name,
                    Number = NormalizePan(card.Number),
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = ToAddress(card.BillingAddress)
                }
            }
        };

        var dto = await SendAsync<PayPalVaultResponseDto>(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            body,
            requestId,
            cancellationToken);

        if (dto is null || string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new OrderPaymentException(502, "PayPal did not return a vaulted payment token.");
        }

        if (string.Equals(dto.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper browser challenge while saving the card. Direct vaulting cannot continue.");
        }

        var saved = dto.PaymentSource?.Card;
        return new PayPalVaultedCard
        {
            VaultId = dto.Id!,
            Last4 = saved?.LastDigits,
            Brand = saved?.Brand,
            Expiry = saved?.Expiry,
            Name = saved?.Name,
            PayPalCustomerId = dto.Customer?.Id
        };
    }

    public async Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<object>(
                HttpMethod.Delete,
                $"v3/vault/payment-tokens/{vaultId}",
                body: null,
                requestId: null,
                cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogInformation("PayPal vault token {VaultId} was already deleted.", vaultId);
        }
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        // Spec: maximum supported range is 31 days.
        var maxWindow = TimeSpan.FromDays(31) - TimeSpan.FromSeconds(1);
        var windowStart = from;

        while (windowStart < to)
        {
            var windowEnd = windowStart + maxWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await CollectWindowAsync(windowStart, windowEnd, results, cancellationToken);
            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private async Task CollectWindowAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        List<PayPalReportedTransaction> sink,
        CancellationToken cancellationToken)
    {
        var page = 1;
        int? totalPages = null;

        do
        {
            var query = QueryString(new Dictionary<string, string?>
            {
                ["start_date"] = FormatReportingTime(from),
                ["end_date"] = FormatReportingTime(to),
                ["page"] = page.ToString(CultureInfo.InvariantCulture),
                ["page_size"] = "500",
                ["fields"] = "all",
                ["balance_affecting_records_only"] = "N"
            });

            var response = await SendAsync<PayPalTransactionSearchResponseDto>(
                HttpMethod.Get,
                "v1/reporting/transactions" + query,
                body: null,
                requestId: null,
                cancellationToken);

            if (response?.TransactionDetails is { Count: > 0 })
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null || string.IsNullOrWhiteSpace(info.TransactionId))
                    {
                        continue;
                    }

                    sink.Add(new PayPalReportedTransaction
                    {
                        TransactionId = info.TransactionId!,
                        ReferenceId = info.PaypalReferenceId,
                        InvoiceId = info.InvoiceId,
                        CustomField = info.CustomField,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        Amount = info.TransactionAmount is null ? null : PayPalMoney.FromValue(info.TransactionAmount.Value),
                        Currency = info.TransactionAmount?.CurrencyCode,
                        Fee = info.FeeAmount is null ? null : PayPalMoney.FromValue(info.FeeAmount.Value),
                        InitiationDate = ParseTime(info.TransactionInitiationDate),
                        InstrumentType = info.InstrumentType
                    });
                }
            }

            var detailsCount = response?.TransactionDetails?.Count ?? 0;
            totalPages = response?.TotalPages;
            if (detailsCount == 0)
            {
                break;
            }

            page++;
            if (totalPages.HasValue && page > totalPages.Value)
            {
                break;
            }

            if (page > 10_000)
            {
                break;
            }
        } while (true);
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken)
    {
        await EnsureAccessTokenAsync(cancellationToken);

        HttpResponseMessage response;
        try
        {
            response = await SendOnceAsync(method, path, body, requestId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OrderPaymentException)
        {
            throw new OrderPaymentException(502, "Failed to call PayPal.", ex);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await RefreshAccessTokenAsync(force: true, cancellationToken);
            response.Dispose();
            response = await SendOnceAsync(method, path, body, requestId, cancellationToken);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "PayPal {Method} {Path} failed with {Status}. debug body omitted of any card data.",
                method.Method, path, (int)response.StatusCode);
            throw PayPalApiException.From(response.StatusCode, content);
        }

        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, PayPalJson.Options);
        }
        catch (JsonException ex)
        {
            throw new OrderPaymentException(502, "PayPal returned a response that could not be read.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", TruncateRequestId(requestId));
        }

        if (body is not null)
        {
            request.Content = PayPalJson.Content(body);
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return;
        }

        await RefreshAccessTokenAsync(force: false, cancellationToken);
    }

    private async Task RefreshAccessTokenAsync(bool force, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!force && !string.IsNullOrWhiteSpace(_accessToken) && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new OrderPaymentException(500, "PayPal credentials are not configured.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw PayPalApiException.From(response.StatusCode, content);
            }

            var token = JsonSerializer.Deserialize<PayPalTokenResponse>(content, PayPalJson.Options);
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                throw new OrderPaymentException(502, "PayPal did not return an access token.");
            }

            _accessToken = token.AccessToken;
            var lifetime = token.ExpiresIn > 0 ? TimeSpan.FromSeconds(token.ExpiresIn) : TimeSpan.FromHours(1);
            _tokenExpiresAt = DateTimeOffset.UtcNow + lifetime - TokenSkew;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static PayPalPaymentSourceDto BuildPaymentSource(PayPalAuthorizeRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.VaultId))
        {
            return new PayPalPaymentSourceDto
            {
                Card = new PayPalCardRequestDto
                {
                    VaultId = request.VaultId,
                    StoredCredential = new PayPalStoredCredentialDto
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "UNSCHEDULED",
                        Usage = "SUBSEQUENT"
                    }
                }
            };
        }

        if (request.Card is null)
        {
            throw new OrderPaymentException(400, "A card or a saved payment method is required to pay.");
        }

        return new PayPalPaymentSourceDto
        {
            Card = new PayPalCardRequestDto
            {
                Name = request.Card.Name,
                Number = NormalizePan(request.Card.Number),
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                BillingAddress = ToAddress(request.Card.BillingAddress)
            }
        };
    }

    private static PayPalAddressDto? ToAddress(CardBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new PayPalAddressDto
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static void EnsureNoPayerActionRequired(PayPalOrderDto order)
    {
        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                $"PayPal required a shopper browser challenge for order {order.Id}. Direct card processing cannot continue.");
        }
    }

    private static bool NeedsAuthorizeCall(string? status) =>
        string.Equals(status, "CREATED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "APPROVED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "SAVED", StringComparison.OrdinalIgnoreCase);

    private static PayPalAuthorizationDto? FirstAuthorization(PayPalOrderDto order) =>
        order.PurchaseUnits?.SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorizationDto>())
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Id));

    private static PayPalAuthorizationDetails ToAuthorizationDetails(PayPalAuthorizationDto? dto, string fallbackId)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Id) && string.IsNullOrWhiteSpace(fallbackId))
        {
            throw new OrderPaymentException(502, "PayPal did not return authorization details.");
        }

        return new PayPalAuthorizationDetails
        {
            AuthorizationId = dto.Id ?? fallbackId,
            Status = dto.Status ?? string.Empty,
            Expiration = ParseTime(dto.ExpirationTime),
            CreateTime = ParseTime(dto.CreateTime),
            Amount = dto.Amount is null ? null : PayPalMoney.FromValue(dto.Amount.Value),
            Currency = dto.Amount?.CurrencyCode
        };
    }

    private static PayPalCaptureResult ToCaptureResult(PayPalCaptureDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new OrderPaymentException(502, "PayPal did not return capture details.");
        }

        var breakdown = dto.SellerReceivableBreakdown;
        var gross = breakdown?.GrossAmount ?? dto.Amount;
        return new PayPalCaptureResult
        {
            CaptureId = dto.Id!,
            Status = dto.Status ?? string.Empty,
            GrossAmount = PayPalMoney.FromValue(gross?.Value),
            PayPalFee = breakdown?.PaypalFee is null ? null : PayPalMoney.FromValue(breakdown.PaypalFee.Value),
            NetAmount = breakdown?.NetAmount is null ? null : PayPalMoney.FromValue(breakdown.NetAmount.Value),
            Currency = gross?.CurrencyCode ?? dto.Amount?.CurrencyCode ?? string.Empty,
            CapturedAt = ParseTime(dto.CreateTime) ?? DateTimeOffset.UtcNow
        };
    }

    private static PayPalMoneyDto Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = PayPalMoney.ToValue(amount)
    };

    private static string NormalizePan(string number) =>
        new string(number.Where(char.IsDigit).ToArray());

    private static string TruncateRequestId(string requestId) =>
        requestId.Length <= 108 ? requestId : requestId[..108];

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

    private static string FormatReportingTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string QueryString(Dictionary<string, string?> values)
    {
        var parts = values
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");
        return "?" + string.Join("&", parts);
    }
}
