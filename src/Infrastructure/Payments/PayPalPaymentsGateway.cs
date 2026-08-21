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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalPaymentsGateway : IPayPalPaymentsGateway
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly PayPalTokenCache _tokenCache;
    private readonly ILogger<PayPalPaymentsGateway> _logger;

    public PayPalPaymentsGateway(
        HttpClient httpClient,
        IOptions<PayPalOptions> options,
        PayPalTokenCache tokenCache,
        ILogger<PayPalPaymentsGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _tokenCache = tokenCache;
        _logger = logger;

        var baseUrl = _options.ResolveBaseUrl();
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        }
    }

    public Task<PayPalAuthorizeResult> AuthorizeCardPaymentAsync(
        int orderId,
        decimal amount,
        string currency,
        IReadOnlyList<PayPalLineItem> items,
        CardPaymentDetails card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSourceDto
        {
            Card = new PayPalCardRequestDto
            {
                Name = string.IsNullOrWhiteSpace(card.Name) ? "Shopper" : card.Name,
                Number = NormalizeCardNumber(card.Number),
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                BillingAddress = ToAddress(card.BillingAddress),
                Attributes = new PayPalCardAttributesDto
                {
                    Verification = new PayPalCardVerificationDto { Method = "AVS_CVV" }
                },
                StoredCredential = new PayPalStoredCredentialDto
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "ONE_TIME",
                    Usage = "FIRST"
                }
            }
        };

        return AuthorizeAsync(orderId, amount, currency, items, paymentSource, requestId, cancellationToken);
    }

    public Task<PayPalAuthorizeResult> AuthorizeVaultedCardPaymentAsync(
        int orderId,
        decimal amount,
        string currency,
        IReadOnlyList<PayPalLineItem> items,
        string vaultId,
        string requestId,
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

        return AuthorizeAsync(orderId, amount, currency, items, paymentSource, requestId, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        return ToAuthorizationDetails(response);
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize",
            new PayPalReauthorizeRequestDto { Amount = Money(amount, currency) },
            requestId,
            cancellationToken);

        return ToAuthorizationDetails(response);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var capture = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture",
            new PayPalCaptureRequestDto
            {
                Amount = Money(amount, currency),
                FinalCapture = true
            },
            requestId,
            cancellationToken);

        if (string.IsNullOrEmpty(capture.Id))
        {
            throw new CommerceException(502, "PayPal capture succeeded but did not return a capture id.");
        }

        var captured = ParseMoney(capture.Amount) ?? amount;
        var fee = ParseMoney(capture.SellerReceivableBreakdown?.PaypalFee) ?? 0m;
        var net = ParseMoney(capture.SellerReceivableBreakdown?.NetAmount) ?? (captured - fee);

        return new PayPalCaptureResult
        {
            CaptureId = capture.Id,
            Status = capture.Status ?? "COMPLETED",
            CapturedAmount = captured,
            PayPalFee = fee,
            NetAmount = net,
            Currency = capture.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void",
            body: null,
            requestId,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var refund = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund",
            new PayPalRefundRequestDto { Amount = Money(amount, currency) },
            requestId,
            cancellationToken);

        if (string.IsNullOrEmpty(refund.Id))
        {
            throw new CommerceException(502, "PayPal refund succeeded but did not return a refund id.");
        }

        return new PayPalRefundResult
        {
            PayPalRefundId = refund.Id,
            Status = refund.Status ?? "COMPLETED",
            Amount = ParseMoney(refund.Amount) ?? amount,
            Currency = refund.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        CardPaymentDetails card,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalVaultRequestDto
        {
            Customer = new PayPalCustomerDto
            {
                Id = string.IsNullOrWhiteSpace(payPalCustomerId) ? null : payPalCustomerId,
                MerchantCustomerId = merchantCustomerId
            },
            PaymentSource = new PayPalVaultPaymentSourceDto
            {
                Card = new PayPalVaultCardDto
                {
                    Name = string.IsNullOrWhiteSpace(card.Name) ? "Shopper" : card.Name,
                    Number = NormalizeCardNumber(card.Number),
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = ToAddress(card.BillingAddress)
                }
            }
        };

        var response = await SendAsync<PayPalVaultResponseDto>(
            HttpMethod.Post,
            "v3/vault/payment-tokens",
            request,
            requestId,
            cancellationToken);

        EnsureNoPayerActionRequired(response.Status, response.Links, "vaulting the card");

        if (string.IsNullOrEmpty(response.Id))
        {
            throw new CommerceException(502, "PayPal did not return a payment token id.");
        }

        var last4 = response.PaymentSource?.Card?.LastDigits;
        if (string.IsNullOrEmpty(last4))
        {
            var number = NormalizeCardNumber(card.Number);
            last4 = number.Length >= 4 ? number[^4..] : number;
        }

        return new PayPalVaultedCard
        {
            PaymentTokenId = response.Id,
            PayPalCustomerId = response.Customer?.Id,
            Last4 = last4,
            Brand = response.PaymentSource?.Card?.Brand,
            Expiry = response.PaymentSource?.Card?.Expiry ?? card.Expiry,
            CardholderName = response.PaymentSource?.Card?.Name ?? card.Name
        };
    }

    public async Task DeletePaymentTokenAsync(
        string paymentTokenId,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalVaultResponseDto>(
            HttpMethod.Delete,
            $"v3/vault/payment-tokens/{paymentTokenId}",
            body: null,
            requestId: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new CommerceException(400, "Reconciliation 'to' must be on or after 'from'.");
        }

        var results = new List<PayPalReportedTransaction>();
        foreach (var (windowStart, windowEnd) in SplitInto31DayWindows(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                var start = FormatReportingDate(windowStart);
                var end = FormatReportingDate(windowEnd);
                var path =
                    $"v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&page_size=500&page={page}&fields=transaction_info&balance_affecting_records_only=N";

                var response = await SendAsync<PayPalTransactionSearchResponseDto>(
                    HttpMethod.Get,
                    path,
                    body: null,
                    requestId: null,
                    cancellationToken);

                if (response.TransactionDetails is not null)
                {
                    results.AddRange(response.TransactionDetails
                        .Where(d => d.TransactionInfo is not null)
                        .Select(d => ToReportedTransaction(d.TransactionInfo!)));
                }

                totalPages = response.TotalPages > 0 ? response.TotalPages : 1;
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<PayPalAuthorizeResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        string currency,
        IReadOnlyList<PayPalLineItem> items,
        PayPalPaymentSourceDto paymentSource,
        string requestId,
        CancellationToken cancellationToken)
    {
        var formattedAmount = FormatAmount(amount);
        var orderRequest = new PayPalCreateOrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequestDto>
            {
                new()
                {
                    ReferenceId = "default",
                    InvoiceId = PayPalOrderIdentifiers.InvoiceId(orderId),
                    CustomId = PayPalOrderIdentifiers.CustomId(orderId),
                    Amount = new PayPalAmountDto
                    {
                        CurrencyCode = currency,
                        Value = formattedAmount,
                        Breakdown = new PayPalAmountBreakdownDto
                        {
                            ItemTotal = Money(amount, currency)
                        }
                    },
                    Items = items.Select(i => new PayPalItemDto
                    {
                        Name = Truncate(i.Name, 127),
                        Quantity = i.Quantity.ToString(CultureInfo.InvariantCulture),
                        UnitAmount = Money(i.UnitPrice, currency)
                    }).ToList()
                }
            },
            PaymentSource = paymentSource
        };

        var created = await SendAsync<PayPalOrderResponseDto>(
            HttpMethod.Post,
            "v2/checkout/orders",
            orderRequest,
            requestId,
            cancellationToken);

        EnsureNoPayerActionRequired(created.Status, created.Links, "authorizing the payment");

        var order = created;
        var authorization = ExtractAuthorization(order);
        if (authorization is null && !string.IsNullOrEmpty(order.Id)
            && !string.Equals(order.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            order = await SendAsync<PayPalOrderResponseDto>(
                HttpMethod.Post,
                $"v2/checkout/orders/{order.Id}/authorize",
                new PayPalAuthorizeRequestDto(),
                requestId,
                cancellationToken);

            EnsureNoPayerActionRequired(order.Status, order.Links, "authorizing the payment");
            authorization = ExtractAuthorization(order);
        }

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new CommerceException(502, "PayPal did not create an authorization hold for this order.");
        }

        return new PayPalAuthorizeResult
        {
            PayPalOrderId = order.Id ?? string.Empty,
            PayPalOrderStatus = order.Status ?? string.Empty,
            AuthorizationId = authorization.Id,
            AuthorizationStatus = authorization.Status,
            AuthorizationExpiresAt = ParseTimestamp(authorization.ExpirationTime),
            AuthorizedAmount = ParseMoney(authorization.Amount) ?? amount,
            Currency = authorization.Amount?.CurrencyCode ?? currency
        };
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool allowNoContent = false) where T : class, new()
    {
        using var request = new HttpRequestMessage(method, relativePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method, RedactPath(relativePath));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || (allowNoContent && string.IsNullOrWhiteSpace(payload)))
        {
            return new T();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw ToCommerceException(response.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return new T();
        }

        return JsonSerializer.Deserialize<T>(payload, SerializerOptions) ?? new T();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_tokenCache.HasValidToken)
        {
            return _tokenCache.AccessToken!;
        }

        await _tokenCache.Gate.WaitAsync(cancellationToken);
        try
        {
            if (_tokenCache.HasValidToken)
            {
                return _tokenCache.AccessToken!;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
            {
                throw new CommerceException(500, "PayPal client credentials are not configured.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
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
                throw ToCommerceException(response.StatusCode, payload);
            }

            var token = JsonSerializer.Deserialize<PayPalAccessTokenResponse>(payload, SerializerOptions);
            if (token is null || string.IsNullOrEmpty(token.AccessToken))
            {
                throw new CommerceException(502, "PayPal did not return an access token.");
            }

            _tokenCache.AccessToken = token.AccessToken;
            _tokenCache.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 30));
            return token.AccessToken;
        }
        finally
        {
            _tokenCache.Gate.Release();
        }
    }

    private CommerceException ToCommerceException(HttpStatusCode statusCode, string payload)
    {
        var redacted = RedactSecrets(payload);
        _logger.LogWarning("PayPal error {Status}: {Body}", (int)statusCode, redacted);

        PayPalErrorBody? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorBody>(payload, SerializerOptions);
        }
        catch (JsonException)
        {
            // Fall through with the raw status.
        }

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

        var details = error?.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(d => d.Issue ?? d.Description).Where(s => !string.IsNullOrEmpty(s)))
            : null;
        var message = error?.Message ?? "PayPal request failed.";
        if (!string.IsNullOrEmpty(error?.Name))
        {
            message = $"{error.Name}: {message}";
        }

        if (!string.IsNullOrEmpty(details))
        {
            message = $"{message} ({details})";
        }

        return new CommerceException(mapped, message);
    }

    private static void EnsureNoPayerActionRequired(string? status, IEnumerable<PayPalLinkDto>? links, string action)
    {
        var hasApproveLink = links?.Any(l =>
            string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)
            || string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase)) == true;

        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || hasApproveLink)
        {
            throw new CommerceException(409,
                $"PayPal required a shopper challenge in the browser while {action}. This integration does not collect a browser approval round-trip.");
        }
    }

    private static PayPalAuthorizationDto? ExtractAuthorization(PayPalOrderResponseDto order)
        => order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorizationDto>())
            .FirstOrDefault(a => !string.IsNullOrEmpty(a.Id));

    private static PayPalAuthorizationDetails ToAuthorizationDetails(PayPalAuthorizationDto dto)
        => new()
        {
            AuthorizationId = dto.Id ?? string.Empty,
            Status = dto.Status ?? string.Empty,
            ExpirationTime = ParseTimestamp(dto.ExpirationTime),
            Amount = ParseMoney(dto.Amount) ?? 0m,
            Currency = dto.Amount?.CurrencyCode ?? string.Empty
        };

    private static PayPalReportedTransaction ToReportedTransaction(PayPalTransactionInfoDto info)
        => new()
        {
            TransactionId = info.TransactionId ?? string.Empty,
            PayPalReferenceId = info.PaypalReferenceId,
            PayPalReferenceIdType = info.PaypalReferenceIdType,
            EventCode = info.TransactionEventCode,
            Status = info.TransactionStatus,
            InvoiceId = info.InvoiceId,
            CustomField = info.CustomField,
            Amount = ParseMoney(info.TransactionAmount),
            FeeAmount = ParseMoney(info.FeeAmount),
            Currency = info.TransactionAmount?.CurrencyCode,
            InitiationDate = ParseTimestamp(info.TransactionInitiationDate)
        };

    private static PayPalAddressDto ToAddress(CardBillingAddress? address)
        => new()
        {
            AddressLine1 = address?.AddressLine1 ?? "123 Main St.",
            AddressLine2 = address?.AddressLine2,
            AdminArea2 = address?.AdminArea2 ?? "San Jose",
            AdminArea1 = address?.AdminArea1 ?? "CA",
            PostalCode = address?.PostalCode ?? "95131",
            CountryCode = string.IsNullOrWhiteSpace(address?.CountryCode) ? "US" : address!.CountryCode
        };

    private static PayPalMoneyDto Money(decimal amount, string currency)
        => new()
        {
            CurrencyCode = currency,
            Value = FormatAmount(amount)
        };

    private static string FormatAmount(decimal amount)
        => decimal.Round(amount, 2).ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(PayPalMoneyDto? money)
    {
        if (money is null || string.IsNullOrWhiteSpace(money.Value))
        {
            return null;
        }

        return decimal.Parse(money.Value, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static string FormatReportingDate(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> SplitInto31DayWindows(DateTimeOffset from, DateTimeOffset to)
    {
        var cursor = from;
        while (true)
        {
            var windowEnd = cursor.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            yield return (cursor, windowEnd);
            if (windowEnd >= to)
            {
                yield break;
            }

            cursor = windowEnd;
        }
    }

    private static string NormalizeCardNumber(string number)
        => string.IsNullOrWhiteSpace(number) ? string.Empty : Regex.Replace(number, @"\s+", "");

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static string RedactPath(string path)
        => path;

    private static string RedactSecrets(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return payload;
        }

        payload = Regex.Replace(payload, "\"number\"\\s*:\\s*\"[^\"]+\"", "\"number\":\"[redacted]\"", RegexOptions.IgnoreCase);
        payload = Regex.Replace(payload, "\"security_code\"\\s*:\\s*\"[^\"]+\"", "\"security_code\":\"[redacted]\"", RegexOptions.IgnoreCase);
        payload = Regex.Replace(payload, "\"access_token\"\\s*:\\s*\"[^\"]+\"", "\"access_token\":\"[redacted]\"", RegexOptions.IgnoreCase);
        return payload;
    }
}
