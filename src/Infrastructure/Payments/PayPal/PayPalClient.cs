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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public sealed class PayPalClient : IPayPalClient
{
    public const string HttpClientName = "PayPal";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly ILogger<PayPalClient> _logger;

    public PayPalClient(
        IHttpClientFactory httpClientFactory,
        PayPalAccessTokenProvider tokenProvider,
        IOptions<PayPalOptions> options,
        ILogger<PayPalClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _logger = logger;
        _ = options;
    }

    public async Task<PayPalOrderSnapshot> CreateAuthorizeOrderAsync(
        CreatePayPalAuthorizeRequest request,
        string paypalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCreateOrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequestDto>
            {
                new()
                {
                    ReferenceId = "default",
                    InvoiceId = request.InvoiceId,
                    CustomId = request.CustomId,
                    Amount = new PayPalAmountRequestDto
                    {
                        CurrencyCode = request.CurrencyCode,
                        Value = request.AmountValue
                    }
                }
            },
            PaymentSource = new PayPalPaymentSourceDto
            {
                Card = BuildCardRequest(request)
            }
        };

        var order = await SendJsonAsync<PayPalOrderDto>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            body,
            paypalRequestId,
            cancellationToken);

        return MapOrder(order);
    }

    public async Task<PayPalOrderSnapshot> AuthorizeOrderAsync(
        string paypalOrderId,
        string paypalRequestId,
        CancellationToken cancellationToken = default)
    {
        var order = await SendJsonAsync<PayPalOrderDto>(
            HttpMethod.Post,
            $"/v2/checkout/orders/{Uri.EscapeDataString(paypalOrderId)}/authorize",
            new { },
            paypalRequestId,
            cancellationToken);

        return MapOrder(order);
    }

    public async Task<PayPalAuthorizationSnapshot> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendJsonAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}",
            null,
            requestId: null,
            cancellationToken);

        return MapAuthorization(dto) ?? throw new PaymentException("PayPal authorization response was missing an id.", 502);
    }

    public async Task<PayPalAuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        string currencyCode,
        string amountValue,
        string paypalRequestId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendJsonAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            new PayPalReauthorizeRequestDto
            {
                Amount = new PayPalMoneyDto { CurrencyCode = currencyCode, Value = amountValue }
            },
            paypalRequestId,
            cancellationToken);

        return MapAuthorization(dto) ?? throw new PaymentException("PayPal reauthorization response was missing an id.", 502);
    }

    public async Task<PayPalCaptureSnapshot> CaptureAuthorizationAsync(
        string authorizationId,
        string paypalRequestId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendJsonAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            new PayPalCaptureRequestDto { FinalCapture = true },
            paypalRequestId,
            cancellationToken);

        return MapCapture(dto) ?? throw new PaymentException("PayPal capture response was missing an id.", 502);
    }

    public async Task<PayPalCaptureSnapshot> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendJsonAsync<PayPalCaptureDto>(
            HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}",
            null,
            requestId: null,
            cancellationToken);

        return MapCapture(dto) ?? throw new PaymentException("PayPal capture response was missing an id.", 502);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string paypalRequestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendJsonAsync<PayPalAuthorizationDto>(
                HttpMethod.Post,
                $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
                null,
                paypalRequestId,
                cancellationToken,
                allowNoContent: true);
        }
        catch (PaymentException ex) when (ex.StatusCode is 404 or 422 or 409)
        {
            _logger.LogInformation("Voiding authorization {AuthorizationId} returned {Status}; treating as already released.", authorizationId, ex.StatusCode);
        }
    }

    public async Task<PayPalRefundSnapshot> RefundCaptureAsync(
        string captureId,
        string? currencyCode,
        string? amountValue,
        string paypalRequestId,
        CancellationToken cancellationToken = default)
    {
        object body;
        if (!string.IsNullOrWhiteSpace(currencyCode) && !string.IsNullOrWhiteSpace(amountValue))
        {
            body = new PayPalRefundRequestDto
            {
                Amount = new PayPalMoneyDto { CurrencyCode = currencyCode, Value = amountValue }
            };
        }
        else
        {
            body = new { };
        }

        var dto = await SendJsonAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            body,
            paypalRequestId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.Status) || dto.Amount?.Value is null)
        {
            throw new PaymentException("PayPal refund response was incomplete.", 502);
        }

        return new PayPalRefundSnapshot
        {
            Id = dto.Id,
            Status = dto.Status,
            Amount = MapMoney(dto.Amount)!,
            CreateTime = ParseTime(dto.CreateTime)
        };
    }

    public async Task<PayPalVaultedCard> CreatePaymentTokenAsync(
        CardPaymentSource card,
        string merchantCustomerId,
        string? paypalCustomerId,
        string paypalRequestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCreatePaymentTokenRequestDto
        {
            Customer = new PayPalVaultCustomerDto
            {
                Id = string.IsNullOrWhiteSpace(paypalCustomerId) ? null : paypalCustomerId,
                MerchantCustomerId = merchantCustomerId
            },
            PaymentSource = new PayPalPaymentSourceDto
            {
                Card = new PayPalCardRequestDto
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };

        var dto = await SendJsonAsync<PayPalPaymentTokenResponseDto>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            body,
            paypalRequestId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.PaymentSource?.Card?.LastDigits))
        {
            throw new PaymentException("PayPal vault response did not include a reusable payment token.", 502);
        }

        return new PayPalVaultedCard
        {
            PaymentTokenId = dto.Id,
            LastDigits = dto.PaymentSource.Card.LastDigits,
            Brand = dto.PaymentSource.Card.Brand,
            Expiry = dto.PaymentSource.Card.Expiry,
            CardholderName = dto.PaymentSource.Card.Name,
            CustomerId = dto.Customer?.Id
        };
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendJsonAsync<object>(
                HttpMethod.Delete,
                $"/v3/vault/payment-tokens/{Uri.EscapeDataString(paymentTokenId)}",
                null,
                requestId: null,
                cancellationToken,
                allowNoContent: true);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            _logger.LogInformation("PayPal payment token {TokenId} was already absent.", paymentTokenId);
        }
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var now = DateTimeOffset.UtcNow;
        if (to > now)
        {
            to = now;
        }

        if (from >= to)
        {
            return results;
        }

        foreach (var (windowStart, windowEnd) in SplitIntoWindows(from, to, TimeSpan.FromDays(30)))
        {
            var page = 1;
            var totalPages = 1;
            do
            {
                var start = FormatPayPalDate(windowStart);
                var end = FormatPayPalDate(windowEnd);
                var path =
                    "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(start)}" +
                    $"&end_date={Uri.EscapeDataString(end)}" +
                    "&fields=transaction_info" +
                    "&page_size=500" +
                    $"&page={page}" +
                    "&balance_affecting_records_only=N";

                PayPalSearchResponseDto response;
                try
                {
                    response = await SendJsonAsync<PayPalSearchResponseDto>(
                        HttpMethod.Get,
                        path,
                        null,
                        requestId: null,
                        cancellationToken);
                }
                catch (PaymentException ex) when (ex.StatusCode == 404)
                {
                    _logger.LogInformation(
                        "PayPal reporting has no data yet for {Start} to {End}. Returning an empty page for this window.",
                        start,
                        end);
                    break;
                }

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null)
                        {
                            continue;
                        }

                        results.Add(new PayPalReportedTransaction
                        {
                            TransactionId = info.TransactionId,
                            ReferenceId = info.PaypalReferenceId,
                            ReferenceIdType = info.PaypalReferenceIdType,
                            EventCode = info.TransactionEventCode,
                            Status = info.TransactionStatus,
                            InvoiceId = info.InvoiceId,
                            CustomField = info.CustomField,
                            InitiationDate = ParseTime(info.TransactionInitiationDate),
                            UpdatedDate = ParseTime(info.TransactionUpdatedDate),
                            Amount = MapTxnMoney(info.TransactionAmount),
                            FeeAmount = MapTxnMoney(info.FeeAmount)
                        });
                    }
                }

                totalPages = response.TotalPages > 0 ? response.TotalPages : 1;
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private PayPalCardRequestDto BuildCardRequest(CreatePayPalAuthorizeRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.VaultId))
        {
            return new PayPalCardRequestDto
            {
                VaultId = request.VaultId,
                StoredCredential = new PayPalCardStoredCredentialDto
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "UNSCHEDULED",
                    Usage = "SUBSEQUENT"
                }
            };
        }

        if (request.Card is null)
        {
            throw new PaymentException("A card or a saved payment method is required to pay.", 400);
        }

        return new PayPalCardRequestDto
        {
            Name = request.Card.Name,
            Number = request.Card.Number,
            Expiry = request.Card.Expiry,
            SecurityCode = request.Card.SecurityCode,
            BillingAddress = MapAddress(request.Card.BillingAddress)
        };
    }

    private static PayPalAddressDto? MapAddress(CardBillingAddress? address)
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

    private static PayPalOrderSnapshot MapOrder(PayPalOrderDto order)
    {
        if (string.IsNullOrWhiteSpace(order.Id) || string.IsNullOrWhiteSpace(order.Status))
        {
            throw new PaymentException("PayPal order response was incomplete.", 502);
        }

        var authorization = order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorizationDto>())
            .Select(MapAuthorization)
            .FirstOrDefault(a => a is not null);

        var payerActionLinks = order.Links?
            .Where(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Href)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Cast<string>()
            .ToArray() ?? Array.Empty<string>();

        return new PayPalOrderSnapshot
        {
            Id = order.Id,
            Status = order.Status,
            Authorization = authorization,
            PayerActionLinks = payerActionLinks
        };
    }

    private static PayPalAuthorizationSnapshot? MapAuthorization(PayPalAuthorizationDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.Status))
        {
            return null;
        }

        return new PayPalAuthorizationSnapshot
        {
            Id = dto.Id,
            Status = dto.Status,
            Amount = MapMoney(dto.Amount) ?? new PayPalMoney { CurrencyCode = "USD", Value = "0.00" },
            CreateTime = ParseTime(dto.CreateTime),
            ExpirationTime = ParseTime(dto.ExpirationTime)
        };
    }

    private static PayPalCaptureSnapshot? MapCapture(PayPalCaptureDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.Status))
        {
            return null;
        }

        return new PayPalCaptureSnapshot
        {
            Id = dto.Id,
            Status = dto.Status,
            Amount = MapMoney(dto.Amount) ?? MapMoney(dto.SellerReceivableBreakdown?.GrossAmount)
                     ?? new PayPalMoney { CurrencyCode = "USD", Value = "0.00" },
            PayPalFee = MapMoney(dto.SellerReceivableBreakdown?.PaypalFee),
            NetAmount = MapMoney(dto.SellerReceivableBreakdown?.NetAmount),
            CreateTime = ParseTime(dto.CreateTime)
        };
    }

    private static PayPalMoney? MapMoney(PayPalMoneyDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.CurrencyCode) || string.IsNullOrWhiteSpace(dto.Value))
        {
            return null;
        }

        return new PayPalMoney { CurrencyCode = dto.CurrencyCode, Value = dto.Value };
    }

    private static PayPalMoney? MapTxnMoney(PayPalTransactionAmountDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.CurrencyCode) || string.IsNullOrWhiteSpace(dto.Value))
        {
            return null;
        }

        return new PayPalMoney { CurrencyCode = dto.CurrencyCode, Value = dto.Value };
    }

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

    private static string FormatPayPalDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitIntoWindows(
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan maxWindow)
    {
        var cursor = from;
        while (cursor < to)
        {
            var windowEnd = cursor + maxWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            // PayPal's maximum supported range is 31 days; keep the window exclusive of overflow.
            if (windowEnd - cursor > maxWindow)
            {
                windowEnd = cursor + maxWindow;
            }

            yield return (cursor, windowEnd);
            cursor = windowEnd;
        }
    }

    private async Task<T> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowNoContent = false)
    {
        var response = await SendOnceAsync(method, path, body, requestId, cancellationToken, retryOnUnauthorized: true);

        if (response.StatusCode == HttpStatusCode.NoContent && allowNoContent)
        {
            return default!;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayPal {Method} {Path} failed with {Status}: {ErrorBody}", method.Method, RedactPath(path), (int)response.StatusCode, payload);
            throw CreateApiException(response.StatusCode, payload);
        }

        if (allowNoContent && string.IsNullOrWhiteSpace(payload))
        {
            return default!;
        }

        var parsed = JsonSerializer.Deserialize<T>(payload, JsonOptions);
        if (parsed is null)
        {
            throw new PaymentException("PayPal returned an empty response body.", 502);
        }

        return parsed;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool retryOnUnauthorized)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, path);
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body is not null && method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method.Method, RedactPath(path));
        var response = await client.SendAsync(request, cancellationToken);

        if (retryOnUnauthorized && response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            _tokenProvider.Invalidate();
            return await SendOnceAsync(method, path, body, requestId, cancellationToken, retryOnUnauthorized: false);
        }

        return response;
    }

    private static string RedactPath(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? path[..q] : path;
    }

    internal static PaymentException CreateApiException(HttpStatusCode statusCode, string payload)
    {
        PayPalErrorBody? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorBody>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            // fall through with a generic message; never echo raw payloads that might contain PAN data
        }

        var message = error?.ToPublicMessage() ?? $"PayPal request failed with status {(int)statusCode}.";
        var mapped = statusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            (HttpStatusCode)422 => 422,
            _ => 502
        };

        return new PaymentException(message, mapped);
    }
}
