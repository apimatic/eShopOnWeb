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
using Microsoft.eShopWeb.Infrastructure.PayPal.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public sealed class PayPalPaymentsClient : IPayPalPaymentsClient
{
    private static readonly TimeSpan MaxTransactionSearchWindow = TimeSpan.FromDays(31);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPayPalAccessTokenProvider _tokenProvider;
    private readonly IOptionsMonitor<PayPalOptions> _options;
    private readonly ILogger<PayPalPaymentsClient> _logger;

    public PayPalPaymentsClient(
        IHttpClientFactory httpClientFactory,
        IPayPalAccessTokenProvider tokenProvider,
        IOptionsMonitor<PayPalOptions> options,
        ILogger<PayPalPaymentsClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options;
        _logger = logger;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeCardPaymentAsync(
        PayPalAuthorizeRequest request,
        CancellationToken cancellationToken = default)
    {
        var createBody = BuildCreateOrderRequest(request);
        var order = await SendAsync<PayPalOrderDto>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            createBody,
            request.RequestId,
            cancellationToken);

        ThrowIfPayerActionRequired(order);

        var authorization = FirstAuthorization(order);
        if (authorization == null && !string.Equals(order.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            order = await SendAsync<PayPalOrderDto>(
                HttpMethod.Post,
                $"/v2/checkout/orders/{order.Id}/authorize",
                new { },
                $"{request.RequestId}-authorize",
                cancellationToken);
            ThrowIfPayerActionRequired(order);
            authorization = FirstAuthorization(order);
        }

        if (authorization?.Id is null)
        {
            throw new PaymentException(
                $"PayPal did not return an authorization for order {order.Id}. Status was '{order.Status}'.",
                502,
                paypalDebugId: null);
        }

        if (string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException("PayPal denied the card authorization.", 402);
        }

        return MapAuthorization(order.Id!, authorization);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            cancellationToken);

        return MapAuthorization(paypalOrderId: string.Empty, authorization);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalReauthorizeRequestDto
        {
            Amount = PayPalMoneyDto.From(amount, currency)
        };

        var authorization = await SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            requestId,
            cancellationToken);

        return MapAuthorization(paypalOrderId: string.Empty, authorization);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalCaptureRequestDto
        {
            Amount = PayPalMoneyDto.From(amount, currency),
            InvoiceId = invoiceId,
            FinalCapture = true
        };

        var capture = await SendAsync<PayPalCaptureDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body,
            requestId,
            cancellationToken);

        if (string.IsNullOrEmpty(capture.Id))
        {
            throw new PaymentException("PayPal capture response did not include an id.", 502);
        }

        if (capture.SellerReceivableBreakdown?.PaypalFee == null
            || capture.SellerReceivableBreakdown?.NetAmount == null)
        {
            var detailed = await SendAsync<PayPalCaptureDto>(
                HttpMethod.Get,
                $"/v2/payments/captures/{capture.Id}",
                body: null,
                requestId: null,
                cancellationToken);
            if (detailed.SellerReceivableBreakdown != null)
            {
                capture.SellerReceivableBreakdown = detailed.SellerReceivableBreakdown;
            }

            if (detailed.Amount != null)
            {
                capture.Amount = detailed.Amount;
            }

            if (!string.IsNullOrEmpty(detailed.Status))
            {
                capture.Status = detailed.Status;
            }
        }

        var capturedAmount = capture.Amount?.ToDecimal() ?? amount;
        var fee = capture.SellerReceivableBreakdown?.PaypalFee?.ToDecimal();
        var net = capture.SellerReceivableBreakdown?.NetAmount?.ToDecimal();

        return new PayPalCaptureResult
        {
            CaptureId = capture.Id,
            Status = capture.Status ?? string.Empty,
            CapturedAmount = capturedAmount,
            Currency = capture.Amount?.CurrencyCode ?? currency,
            PaypalFee = fee,
            NetAmount = net
        };
    }

    public Task VoidAuthorizationAsync(
        string authorizationId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<PayPalAuthorizationDto>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            body: null,
            requestId,
            cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string invoiceId,
        string customId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalRefundRequestDto
        {
            Amount = amount.HasValue ? PayPalMoneyDto.From(amount.Value, currency) : null,
            InvoiceId = invoiceId,
            CustomId = customId
        };

        var refund = await SendAsync<PayPalRefundDto>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            requestId,
            cancellationToken);

        if (refund.Id is null)
        {
            throw new PaymentException("PayPal refund response did not include an id.", 502);
        }

        return new PayPalRefundResult
        {
            RefundId = refund.Id,
            Status = refund.Status ?? string.Empty,
            Amount = refund.Amount?.ToDecimal() ?? amount ?? 0m,
            Currency = refund.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<PayPalVaultedCardResult> VaultCardAsync(
        PayPalVaultCardRequest request,
        CancellationToken cancellationToken = default)
    {
        var body = new PayPalVaultPaymentTokenRequestDto
        {
            Customer = string.IsNullOrWhiteSpace(request.MerchantCustomerId)
                ? null
                : new PayPalCustomerDto { MerchantCustomerId = SanitizeMerchantCustomerId(request.MerchantCustomerId) },
            PaymentSource = new PayPalVaultPaymentSourceDto
            {
                Card = MapCard(request.Card)
            }
        };

        var token = await SendAsync<PayPalVaultPaymentTokenDto>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            body,
            request.RequestId,
            cancellationToken);

        var card = token.PaymentSource?.Card;
        if (token.Id is null || card?.LastDigits is null)
        {
            throw new PaymentException("PayPal vault response did not include a payment token id and last digits.", 502);
        }

        return new PayPalVaultedCardResult
        {
            VaultId = token.Id,
            LastDigits = card.LastDigits,
            Brand = card.Brand ?? "CARD",
            Expiry = card.Expiry,
            Name = card.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        var response = await SendRawAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{vaultId}",
            body: null,
            requestId: null,
            cancellationToken,
            treatNotFoundAsSuccess: true);

        response.Dispose();
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        foreach (var (windowStart, windowEnd) in SplitDateRange(from, to))
        {
            var page = 1;
            int totalPages;
            do
            {
                var start = FormatPaypalTimestamp(windowStart);
                var end = FormatPaypalTimestamp(windowEnd);
                var path =
                    $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=all&page_size=100&page={page}&balance_affecting_records_only=N";

                var search = await SendAsync<PayPalTransactionSearchResponseDto>(
                    HttpMethod.Get,
                    path,
                    body: null,
                    requestId: null,
                    cancellationToken);

                if (search.TransactionDetails != null)
                {
                    foreach (var detail in search.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info?.TransactionId is null)
                        {
                            continue;
                        }

                        results.Add(new PayPalReportedTransaction
                        {
                            TransactionId = info.TransactionId,
                            ReferenceId = info.PaypalReferenceId,
                            CustomField = info.CustomField,
                            InvoiceId = info.InvoiceId,
                            Status = info.TransactionStatus,
                            EventCode = info.TransactionEventCode,
                            InitiationDate = ParseTimestamp(info.TransactionInitiationDate),
                            Amount = info.TransactionAmount?.ToDecimal(),
                            FeeAmount = info.FeeAmount?.ToDecimal(),
                            Currency = info.TransactionAmount?.CurrencyCode
                        });
                    }
                }

                totalPages = search.TotalPages.GetValueOrDefault(1);
                if (totalPages < 1)
                {
                    totalPages = 1;
                }

                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private PayPalCreateOrderRequestDto BuildCreateOrderRequest(PayPalAuthorizeRequest request)
    {
        var currency = request.Currency;
        var amountValue = request.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        var items = request.Items.Select(item => new PayPalItemDto
        {
            Name = Truncate(item.Name, 127),
            Sku = item.Sku,
            Quantity = item.Quantity.ToString(CultureInfo.InvariantCulture),
            Category = "PHYSICAL_GOODS",
            UnitAmount = PayPalMoneyDto.From(item.UnitAmount, currency)
        }).ToArray();

        var card = request.Card != null
            ? MapCard(request.Card)
            : request.VaultId != null
                ? new PayPalCardRequestDto
                {
                    VaultId = request.VaultId,
                    StoredCredential = new PayPalStoredCredentialDto
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "UNSCHEDULED",
                        Usage = "SUBSEQUENT"
                    }
                }
                : throw new PaymentException("A card or a saved payment method is required to authorize payment.");

        return new PayPalCreateOrderRequestDto
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new[]
            {
                new PayPalPurchaseUnitRequestDto
                {
                    ReferenceId = "default",
                    Description = $"eShopOnWeb order {request.CustomId}",
                    CustomId = request.CustomId,
                    InvoiceId = request.InvoiceId,
                    Amount = new PayPalAmountDto
                    {
                        CurrencyCode = currency,
                        Value = amountValue,
                        Breakdown = new PayPalAmountBreakdownDto
                        {
                            ItemTotal = PayPalMoneyDto.From(request.Amount, currency)
                        }
                    },
                    Items = items,
                    Shipping = request.Shipping == null
                        ? null
                        : new PayPalShippingDto
                        {
                            Name = string.IsNullOrWhiteSpace(request.Shipping.FullName)
                                ? null
                                : new PayPalShippingNameDto { FullName = request.Shipping.FullName },
                            Address = MapAddress(request.Shipping)
                        }
                }
            },
            PaymentSource = new PayPalPaymentSourceDto { Card = card }
        };
    }

    private static PayPalCardRequestDto MapCard(PayPalCardDetails card)
    {
        return new PayPalCardRequestDto
        {
            Name = card.Name,
            Number = new string(card.Number.Where(char.IsDigit).ToArray()),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = card.BillingAddress == null ? null : MapAddress(card.BillingAddress)
        };
    }

    private static PayPalAddressDto MapAddress(PayPalShippingAddress address)
    {
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

    private static PayPalAuthorizationDto? FirstAuthorization(PayPalOrderDto order)
    {
        return order.PurchaseUnits?
            .SelectMany(unit => unit.Payments?.Authorizations ?? Array.Empty<PayPalAuthorizationDto>())
            .FirstOrDefault(a => a.Id != null);
    }

    private static PayPalAuthorizationResult MapAuthorization(string paypalOrderId, PayPalAuthorizationDto authorization)
    {
        if (authorization.Id is null)
        {
            throw new PaymentException("PayPal authorization response did not include an id.", 502);
        }

        return new PayPalAuthorizationResult
        {
            PaypalOrderId = paypalOrderId,
            AuthorizationId = authorization.Id,
            Status = authorization.Status ?? string.Empty,
            ExpirationTime = ParseTimestamp(authorization.ExpirationTime),
            CreateTime = ParseTimestamp(authorization.CreateTime),
            Amount = authorization.Amount?.ToDecimal() ?? 0m,
            Currency = authorization.Amount?.CurrencyCode ?? string.Empty
        };
    }

    private static void ThrowIfPayerActionRequired(PayPalOrderDto order)
    {
        if (!string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            var payerAction = order.Links?.FirstOrDefault(l =>
                string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase));
            if (payerAction == null)
            {
                return;
            }
        }

        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || order.Links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true)
        {
            var url = order.Links?.FirstOrDefault(l =>
                string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase))?.Href;
            throw new PayerActionRequiredException(order.Id ?? string.Empty, url);
        }
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken) where T : class
    {
        using var response = await SendRawAsync(method, path, body, requestId, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return Activator.CreateInstance<T>();
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Activator.CreateInstance<T>();
        }

        var parsed = JsonSerializer.Deserialize<T>(payload, PayPalJson.Options);
        return parsed ?? Activator.CreateInstance<T>();
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string path,
        object? body,
        string? requestId,
        CancellationToken cancellationToken,
        bool treatNotFoundAsSuccess = false)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            response?.Dispose();
            var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, $"{_options.CurrentValue.GetApiBaseUrl()}{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrEmpty(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            if (body != null)
            {
                var json = JsonSerializer.Serialize(body, PayPalJson.Options);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var client = _httpClientFactory.CreateClient(nameof(PayPalPaymentsClient));
            response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _tokenProvider.Invalidate();
                continue;
            }

            if ((int)response.StatusCode == 429 || ((int)response.StatusCode >= 500 && attempt < 3))
            {
                var delayMs = (int)(Math.Pow(2, attempt) * 250 + Random.Shared.Next(50, 150));
                _logger.LogWarning("PayPal returned {StatusCode} for {Method} {Path}. Retrying in {Delay}ms.",
                    (int)response.StatusCode, method, RedactPath(path), delayMs);
                await Task.Delay(delayMs, cancellationToken);
                continue;
            }

            if (treatNotFoundAsSuccess && response.StatusCode == HttpStatusCode.NotFound)
            {
                return response;
            }

            if (!response.IsSuccessStatusCode)
            {
                await ThrowPaypalErrorAsync(response, cancellationToken);
            }

            return response;
        }

        await ThrowPaypalErrorAsync(response!, cancellationToken);
        return response!;
    }

    private async Task ThrowPaypalErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        PayPalErrorDto? error = null;
        try
        {
            error = JsonSerializer.Deserialize<PayPalErrorDto>(payload, PayPalJson.Options);
        }
        catch (JsonException)
        {
            // PayPal sometimes returns a non-JSON body; never log it in case it contains card data.
        }

        if (error?.DebugId != null)
        {
            _logger.LogError(
                "PayPal API error {Name}: {Message} (debug_id {DebugId}, status {StatusCode})",
                error.Name,
                error.Message,
                error.DebugId,
                (int)response.StatusCode);
        }
        else
        {
            _logger.LogError("PayPal API error with status {StatusCode}.", (int)response.StatusCode);
        }

        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var description = error?.Details?.FirstOrDefault()?.Description;
        var message = error?.Message
            ?? "PayPal request failed.";
        if (!string.IsNullOrWhiteSpace(issue))
        {
            message = $"{message} ({issue}{(string.IsNullOrWhiteSpace(description) ? string.Empty : $": {description}")})";
        }

        var statusCode = (int)response.StatusCode;
        if (statusCode == 422 || statusCode == 409)
        {
            statusCode = 409;
        }
        else if (statusCode >= 500)
        {
            statusCode = 502;
        }
        else if (statusCode == 401 || statusCode == 403)
        {
            statusCode = 502;
        }

        throw new PaymentException(message, statusCode, error?.DebugId);
    }

    private static string RedactPath(string path)
    {
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        return queryIndex < 0 ? path : path[..queryIndex];
    }

    private static string Truncate(string value, int max)
    {
        return value.Length <= max ? value : value[..max];
    }

    private static string SanitizeMerchantCustomerId(string buyerId)
    {
        var builder = new StringBuilder();
        foreach (var ch in buyerId)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '^' or '*' or '$' or '@' or '#')
            {
                builder.Append(ch);
            }
        }

        var sanitized = builder.ToString();
        if (sanitized.Length > 64)
        {
            sanitized = sanitized[..64];
        }

        return string.IsNullOrEmpty(sanitized) ? "eshop-buyer" : sanitized;
    }

    public static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> SplitDateRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (from > to)
        {
            yield break;
        }

        var cursor = from;
        while (true)
        {
            var windowEnd = cursor.Add(MaxTransactionSearchWindow);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            yield return (cursor, windowEnd);
            if (windowEnd >= to)
            {
                yield break;
            }

            cursor = windowEnd.AddSeconds(1);
        }
    }

    private static string FormatPaypalTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
