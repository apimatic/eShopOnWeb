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

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan MaxTransactionSearchWindow = TimeSpan.FromDays(31);
    private const int TransactionPageSize = 100;
    private const int MaxRetries = 3;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly IOptions<PayPalOptions> _options;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(
        IHttpClientFactory httpClientFactory,
        PayPalAccessTokenProvider tokenProvider,
        IOptions<PayPalOptions> options,
        ILogger<PayPalGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options;
        _logger = logger;
    }

    public string Currency =>
        string.IsNullOrWhiteSpace(_options.Value.Currency)
            ? throw new PayPalApiException("PayPal:Currency is not configured.")
            : _options.Value.Currency;

    public async Task<string> CreateAuthorizedOrderAsync(
        decimal amount,
        string invoiceId,
        string customId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var payload = new PayPalOrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequestDto>
            {
                new()
                {
                    InvoiceId = invoiceId,
                    CustomId = customId,
                    Amount = Money(amount)
                }
            }
        };

        var order = await SendJsonAsync<PayPalOrderResponseDto>(
            HttpMethod.Post,
            "v2/checkout/orders",
            payload,
            requestId,
            cancellationToken);

        if (string.IsNullOrEmpty(order.Id))
        {
            throw new PayPalApiException("PayPal create-order response did not include an id.");
        }

        EnsureNoPayerAction(order.Status, order.Links);
        return order.Id;
    }

    public Task<AuthorizedPaymentResult> AuthorizeCardAsync(
        string payPalOrderId,
        CardPaymentSource card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalAuthorizeRequestDto
        {
            PaymentSource = new PayPalPaymentSourceDto
            {
                Card = new PayPalCardDto
                {
                    Name = card.Name,
                    Number = NormalizePan(card.Number),
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };

        return AuthorizeAsync(payPalOrderId, request, requestId, cancellationToken);
    }

    public Task<AuthorizedPaymentResult> AuthorizeVaultedCardAsync(
        string payPalOrderId,
        VaultedCardPaymentSource vaultedCard,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalAuthorizeRequestDto
        {
            PaymentSource = new PayPalPaymentSourceDto
            {
                Card = new PayPalCardDto
                {
                    VaultId = vaultedCard.VaultId,
                    StoredCredential = new PayPalStoredCredentialDto
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "UNSCHEDULED",
                        Usage = "SUBSEQUENT"
                    }
                }
            }
        };

        return AuthorizeAsync(payPalOrderId, request, requestId, cancellationToken);
    }

    public async Task<AuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendJsonAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}",
            null,
            requestId: null,
            cancellationToken);

        return MapAuthorization(dto);
    }

    public async Task<AuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendJsonAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            new PayPalReauthorizeRequestDto { Amount = Money(amount) },
            requestId,
            cancellationToken);

        return MapAuthorization(dto);
    }

    public async Task<CapturedPaymentResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var dto = await SendJsonAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            new PayPalCaptureRequestDto
            {
                Amount = Money(amount),
                InvoiceId = invoiceId,
                FinalCapture = true
            },
            requestId,
            cancellationToken);

        dto = await HydrateCaptureAsync(dto, cancellationToken);
        return MapCapture(dto, amount, authorizationId);
    }

    public async Task<CapturedPaymentResult> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken = default)
    {
        var dto = await GetCaptureDtoAsync(captureId, cancellationToken);
        return MapCapture(dto, expectedAmount: null, expectedAuthorizationId: null);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        await SendJsonAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void",
            payload: null,
            requestId,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<RefundPaymentResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        object payload = amount.HasValue
            ? new PayPalRefundRequestDto { Amount = Money(amount.Value) }
            : new { };

        var dto = await SendJsonAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            payload,
            requestId,
            cancellationToken);

        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new PayPalApiException("PayPal refund response did not include an id.");
        }

        return new RefundPaymentResult(
            dto.Id,
            dto.Status ?? "COMPLETED",
            ParseAmount(dto.Amount?.Value) ?? amount ?? 0m,
            dto.Amount?.CurrencyCode ?? Currency);
    }

    public async Task<VaultedCardResult> VaultCardAsync(
        CardPaymentSource card,
        string merchantCustomerId,
        string? payPalCustomerId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var customer = new PayPalCustomerDto { MerchantCustomerId = merchantCustomerId };
        if (!string.IsNullOrWhiteSpace(payPalCustomerId) && payPalCustomerId.Length <= 22)
        {
            customer.Id = payPalCustomerId;
        }

        var payload = new PayPalVaultRequestDto
        {
            PaymentSource = new PayPalPaymentSourceDto
            {
                Card = new PayPalCardDto
                {
                    Name = card.Name,
                    Number = NormalizePan(card.Number),
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            },
            Customer = customer
        };

        var dto = await SendJsonAsync<PayPalVaultResponseDto>(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            payload,
            requestId,
            cancellationToken);

        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new PayPalApiException("PayPal vault response did not include a payment token id.");
        }

        var vaultedCard = dto.PaymentSource?.Card;
        var lastDigits = vaultedCard?.LastDigits;
        if (string.IsNullOrEmpty(lastDigits))
        {
            var pan = NormalizePan(card.Number);
            lastDigits = pan.Length >= 4 ? pan[^4..] : pan;
        }

        return new VaultedCardResult(
            dto.Id,
            dto.Customer?.Id,
            lastDigits,
            vaultedCard?.Brand ?? "CARD",
            vaultedCard?.Expiry ?? card.Expiry,
            vaultedCard?.Name ?? card.Name);
    }

    public async Task DeletePaymentTokenAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default)
    {
        await SendJsonAsync<PayPalVaultResponseDto>(
            HttpMethod.Delete,
            $"v3/vault/payment-tokens/{paymentTokenId}",
            payload: null,
            requestId: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxTransactionSearchWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await ListTransactionsForWindowAsync(windowStart, windowEnd, results, cancellationToken);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task ListTransactionsForWindowAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        List<PayPalReportedTransaction> sink,
        CancellationToken cancellationToken)
    {
        var page = 1;
        int totalPages;
        do
        {
            var start = Uri.EscapeDataString(FormatTimestamp(from));
            var end = Uri.EscapeDataString(FormatTimestamp(to));
            var path =
                $"v1/reporting/transactions?start_date={start}&end_date={end}&fields=all&page_size={TransactionPageSize}&page={page}&balance_affecting_records_only=N";

            var response = await SendJsonAsync<PayPalTransactionSearchResponseDto>(
                HttpMethod.Get,
                path,
                payload: null,
                requestId: null,
                cancellationToken);

            if (response.TransactionDetails != null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId == null)
                    {
                        continue;
                    }

                    sink.Add(new PayPalReportedTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.InvoiceId,
                        info.CustomField,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        ParseTimestamp(info.TransactionInitiationDate),
                        ParseAmount(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        ParseAmount(info.FeeAmount?.Value)));
                }
            }

            totalPages = response.TotalPages.GetValueOrDefault(1);
            page++;
        } while (page <= totalPages);
    }

    private async Task<AuthorizedPaymentResult> AuthorizeAsync(
        string payPalOrderId,
        PayPalAuthorizeRequestDto request,
        string requestId,
        CancellationToken cancellationToken)
    {
        var order = await SendJsonAsync<PayPalOrderResponseDto>(
            HttpMethod.Post,
            $"v2/checkout/orders/{payPalOrderId}/authorize",
            request,
            requestId,
            cancellationToken);

        EnsureNoPayerAction(order.Status, order.Links);

        var authorization = order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorizationDto>())
            .FirstOrDefault();

        if (authorization?.Id == null)
        {
            throw new PayPalApiException("PayPal authorize response did not include an authorization id.");
        }

        return new AuthorizedPaymentResult(
            order.Id ?? payPalOrderId,
            order.Status ?? "COMPLETED",
            authorization.Id,
            authorization.Status ?? "CREATED",
            ParseTimestamp(authorization.ExpirationTime),
            ParseAmount(authorization.Amount?.Value) ?? 0m,
            authorization.Amount?.CurrencyCode ?? Currency);
    }

    private async Task<T> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        object? payload,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowNoContent = false) where T : class, new()
    {
        var attempt = 0;
        var retriedUnauthorized = false;
        while (true)
        {
            attempt++;
            var client = _httpClientFactory.CreateClient("PayPal");
            using var httpRequest = new HttpRequestMessage(method, path);
            var token = await _tokenProvider.GetTokenAsync(cancellationToken);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrEmpty(requestId))
            {
                httpRequest.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            if (payload != null)
            {
                var json = JsonSerializer.Serialize(payload, PayPalJson.Options);
                var content = new StringContent(json, Encoding.UTF8);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                httpRequest.Content = content;
            }

            _logger.LogInformation("PayPal {Method} {Path}", method.Method, RedactPath(path));

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var body = response.Content.Headers.ContentLength is 0
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && !retriedUnauthorized)
            {
                retriedUnauthorized = true;
                _tokenProvider.Invalidate();
                continue;
            }

            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
            {
                if (attempt < MaxRetries && (method == HttpMethod.Get || !string.IsNullOrEmpty(requestId)))
                {
                    var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1) + Random.Shared.Next(50, 150));
                    _logger.LogWarning(
                        "PayPal {Method} {Path} returned {Status}; retrying after {Delay}ms",
                        method.Method,
                        RedactPath(path),
                        (int)response.StatusCode,
                        (int)delay.TotalMilliseconds);
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }
            }

            if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(body))
            {
                if (allowNoContent && response.IsSuccessStatusCode)
                {
                    return new T();
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw ToApiException(response.StatusCode, body);
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                throw ToApiException(response.StatusCode, body);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return new T();
            }

            try
            {
                return JsonSerializer.Deserialize<T>(body, PayPalJson.Options) ?? new T();
            }
            catch (JsonException ex)
            {
                throw new PayPalApiException($"PayPal returned a response that could not be parsed: {ex.Message}");
            }
        }
    }

    private PayPalApiException ToApiException(HttpStatusCode statusCode, string body)
    {
        PayPalErrorDto? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorDto>(body, PayPalJson.Options);
        }
        catch (JsonException)
        {
            // Body is not the standard error document.
        }

        var parts = new List<string>();
        if (error?.Details != null)
        {
            foreach (var d in error.Details)
            {
                var piece = d.Issue ?? "DETAIL";
                if (!string.IsNullOrEmpty(d.Field))
                {
                    piece += $" at {d.Field}";
                }
                if (!string.IsNullOrEmpty(d.Description))
                {
                    piece += $": {d.Description}";
                }
                parts.Add(piece);
            }
        }

        var message = error?.Message
                      ?? (string.IsNullOrWhiteSpace(body) ? statusCode.ToString() : "PayPal request failed.");
        if (parts.Count > 0)
        {
            message = $"{message} ({string.Join("; ", parts)})";
        }

        _logger.LogWarning(
            "PayPal request failed with {StatusCode} name={Name} debug_id={DebugId} issue={Issue}",
            (int)statusCode,
            error?.Name,
            error?.DebugId,
            parts.Count > 0 ? parts[0] : null);

        return new PayPalApiException(message, error?.DebugId, statusCode);
    }

    private static void EnsureNoPayerAction(string? status, IEnumerable<PayPalLinkDto>? links)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException();
        }

        if (links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new PayerActionRequiredException();
        }
    }

    private PayPalMoneyDto Money(decimal amount) =>
        new()
        {
            CurrencyCode = Currency,
            Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
        };

    private static PayPalAddressDto? MapAddress(CardBillingAddress? address)
    {
        if (address == null || string.IsNullOrWhiteSpace(address.CountryCode))
        {
            return null;
        }

        return new PayPalAddressDto
        {
            CountryCode = address.CountryCode,
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode
        };
    }

    private static AuthorizationDetails MapAuthorization(PayPalAuthorizationDto dto)
    {
        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new PayPalApiException("PayPal authorization response did not include an id.");
        }

        return new AuthorizationDetails(
            dto.Id,
            dto.Status ?? "CREATED",
            ParseTimestamp(dto.ExpirationTime),
            ParseTimestamp(dto.CreateTime),
            ParseAmount(dto.Amount?.Value) ?? 0m,
            dto.Amount?.CurrencyCode ?? string.Empty,
            dto.SupplementaryData?.RelatedIds?.CaptureId);
    }

    private async Task<PayPalCaptureDto> HydrateCaptureAsync(
        PayPalCaptureDto dto,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new PayPalApiException("PayPal capture response did not include an id.");
        }

        var captured = ParseAmount(dto.Amount?.Value)
                       ?? ParseAmount(dto.SellerReceivableBreakdown?.GrossAmount?.Value);
        if (captured is not null && dto.SellerReceivableBreakdown?.PaypalFee is not null)
        {
            return dto;
        }

        return await GetCaptureDtoAsync(dto.Id, cancellationToken);
    }

    private async Task<PayPalCaptureDto> GetCaptureDtoAsync(
        string captureId,
        CancellationToken cancellationToken)
    {
        var dto = await SendJsonAsync<PayPalCaptureDto>(
            HttpMethod.Get,
            $"v2/payments/captures/{captureId}",
            null,
            requestId: null,
            cancellationToken);

        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new PayPalApiException("PayPal capture response did not include an id.");
        }

        return dto;
    }

    private CapturedPaymentResult MapCapture(
        PayPalCaptureDto dto,
        decimal? expectedAmount,
        string? expectedAuthorizationId)
    {
        if (string.IsNullOrEmpty(dto.Id))
        {
            throw new PayPalApiException("PayPal capture response did not include an id.");
        }

        var captured = ParseAmount(dto.Amount?.Value)
                       ?? ParseAmount(dto.SellerReceivableBreakdown?.GrossAmount?.Value);
        if (captured is null)
        {
            throw new PayPalApiException($"PayPal capture {dto.Id} did not include an amount.");
        }

        var relatedAuth = dto.SupplementaryData?.RelatedIds?.AuthorizationId;
        if (!string.IsNullOrEmpty(expectedAuthorizationId) &&
            !string.IsNullOrEmpty(relatedAuth) &&
            !string.Equals(relatedAuth, expectedAuthorizationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalApiException(
                $"PayPal returned capture {dto.Id} for authorization {relatedAuth}, not {expectedAuthorizationId}.");
        }

        if (expectedAmount.HasValue && captured.Value != expectedAmount.Value)
        {
            throw new PayPalApiException(
                $"PayPal captured {captured.Value} {dto.Amount?.CurrencyCode ?? Currency} but the order total is {expectedAmount.Value} {Currency}.");
        }

        var fee = ParseAmount(dto.SellerReceivableBreakdown?.PaypalFee?.Value) ?? 0m;
        var net = ParseAmount(dto.SellerReceivableBreakdown?.NetAmount?.Value)
                  ?? decimal.Round(captured.Value - fee, 2, MidpointRounding.AwayFromZero);

        return new CapturedPaymentResult(
            dto.Id,
            dto.Status ?? "COMPLETED",
            captured.Value,
            fee,
            net,
            dto.Amount?.CurrencyCode
            ?? dto.SellerReceivableBreakdown?.GrossAmount?.CurrencyCode
            ?? Currency);
    }

    private static string NormalizePan(string number) =>
        new string(number.Where(char.IsDigit).ToArray());

    private static decimal? ParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.Round(decimal.Parse(value, CultureInfo.InvariantCulture), 2, MidpointRounding.AwayFromZero);
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string RedactPath(string path)
    {
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? path[..q] : path;
    }
}
