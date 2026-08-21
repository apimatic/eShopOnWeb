using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalGateway : IPayPalGateway
{
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(HttpClient httpClient, IOptions<PayPalOptions> options, ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency => string.IsNullOrWhiteSpace(_options.Currency) ? "USD" : _options.Currency;

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizePaymentCommand command, CancellationToken cancellationToken = default)
    {
        var request = new PayPalOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits =
            {
                new PayPalPurchaseUnitRequest
                {
                    ReferenceId = "default",
                    Amount = Money(command.Amount, command.Currency),
                    InvoiceId = command.InvoiceId,
                    CustomId = command.CustomId
                }
            },
            PaymentSource = new PayPalPaymentSource
            {
                Card = BuildCard(command.Card, command.VaultId)
            }
        };

        var order = await SendAsync<PayPalOrderResponse>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            request,
            command.IdempotencyKey,
            cancellationToken);

        if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            HasPayerActionLink(order))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper browser challenge (3-D Secure / payer-action) to complete the card payment. This integration does not implement an approval round-trip.");
        }

        var authorization = FirstAuthorization(order);
        if (authorization == null &&
            (string.Equals(order.Status, "CREATED", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(order.Status, "APPROVED", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(order.Status, "SAVED", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrEmpty(order.Id))
            {
                throw new OrderPaymentException("PayPal did not return an order id for authorization.", 502, "PAYPAL_ORDER_MISSING");
            }

            order = await SendAsync<PayPalOrderResponse>(
                HttpMethod.Post,
                $"/v2/checkout/orders/{order.Id}/authorize",
                new { },
                $"{command.IdempotencyKey}-authorize",
                cancellationToken);

            if (string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                HasPayerActionLink(order))
            {
                throw new PayerActionRequiredException(
                    "PayPal required a shopper browser challenge (3-D Secure / payer-action) to complete the card payment. This integration does not implement an approval round-trip.");
            }

            authorization = FirstAuthorization(order);
        }

        if (authorization == null || string.IsNullOrEmpty(authorization.Id) || string.IsNullOrEmpty(order.Id))
        {
            throw new OrderPaymentException(
                $"PayPal did not authorize the payment (order status {order.Status}).",
                502,
                "PAYPAL_AUTHORIZATION_MISSING");
        }

        if (string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new OrderPaymentException(
                $"PayPal declined the authorization ({authorization.Status}).",
                402,
                authorization.Status);
        }

        return new AuthorizationResult(
            order.Id,
            authorization.Id,
            authorization.Status ?? "CREATED",
            PayPalMoneyFormatter.Parse(authorization.Amount?.Value),
            authorization.Amount?.CurrencyCode ?? command.Currency,
            ParseTime(authorization.ExpirationTime),
            ParseTime(authorization.CreateTime));
    }

    public async Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var auth = await SendAsync<PayPalAuthorization>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            null,
            null,
            cancellationToken);

        return ToSnapshot(auth);
    }

    public async Task<AuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new PayPalReauthorizeRequest { Amount = Money(amount, currency) };
        var auth = await SendAsync<PayPalAuthorization>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            body,
            idempotencyKey,
            cancellationToken);

        return ToSnapshot(auth);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string invoiceId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new PayPalCaptureRequest
        {
            Amount = Money(amount, currency),
            InvoiceId = invoiceId,
            FinalCapture = true
        };

        var capture = await SendAsync<PayPalCapture>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            body,
            idempotencyKey,
            cancellationToken);

        return ToCaptureResult(capture, currency);
    }

    public async Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken = default)
    {
        var capture = await SendAsync<PayPalCapture>(
            HttpMethod.Get,
            $"/v2/payments/captures/{captureId}",
            null,
            null,
            cancellationToken);

        return ToCaptureResult(capture, capture.Amount?.CurrencyCode ?? Currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            null,
            idempotencyKey,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw MapError(response.StatusCode, body);
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        object? body = amount.HasValue
            ? new PayPalRefundRequest { Amount = Money(amount.Value, currency) }
            : new { };

        var refund = await SendAsync<PayPalRefund>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            body,
            idempotencyKey,
            cancellationToken);

        if (string.IsNullOrEmpty(refund.Id))
        {
            throw new OrderPaymentException("PayPal did not return a refund id.", 502, "PAYPAL_REFUND_MISSING");
        }

        return new RefundResult(
            refund.Id,
            refund.Status ?? "COMPLETED",
            PayPalMoneyFormatter.Parse(refund.Amount?.Value),
            refund.Amount?.CurrencyCode ?? currency);
    }

    public async Task<VaultedCardResult> VaultCardAsync(CardPaymentDetails card, string merchantCustomerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var setup = await SendAsync<PayPalSetupTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/setup-tokens",
            new PayPalSetupTokenRequest
            {
                Customer = new PayPalCustomer { MerchantCustomerId = merchantCustomerId },
                PaymentSource = new PayPalVaultPaymentSource { Card = ToVaultCard(card) }
            },
            $"{idempotencyKey}-setup",
            cancellationToken);

        if (string.Equals(setup.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            HasPayerActionLink(setup.Links))
        {
            throw new PayerActionRequiredException(
                "PayPal required a shopper browser challenge to vault the card. This integration does not implement an approval round-trip.");
        }

        if (string.IsNullOrEmpty(setup.Id))
        {
            throw new OrderPaymentException("PayPal did not return a setup token.", 502, "PAYPAL_SETUP_TOKEN_MISSING");
        }

        var token = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            new PayPalPaymentTokenRequest
            {
                Customer = new PayPalCustomer
                {
                    Id = setup.Customer?.Id,
                    MerchantCustomerId = merchantCustomerId
                },
                PaymentSource = new PayPalVaultPaymentSource
                {
                    Token = new PayPalVaultToken { Id = setup.Id, Type = "SETUP_TOKEN" }
                }
            },
            $"{idempotencyKey}-token",
            cancellationToken);

        if (string.IsNullOrEmpty(token.Id))
        {
            throw new OrderPaymentException("PayPal did not return a payment token.", 502, "PAYPAL_PAYMENT_TOKEN_MISSING");
        }

        var vaultedCard = token.PaymentSource?.Card;
        return new VaultedCardResult(
            token.Id,
            token.Customer?.Id ?? setup.Customer?.Id,
            vaultedCard?.Brand,
            vaultedCard?.LastDigits,
            vaultedCard?.Expiry,
            vaultedCard?.Name ?? card.Name);
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{paymentTokenId}",
            null,
            null,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.NotFound)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw MapError(response.StatusCode, body);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        foreach (var window in SplitWindows(from, to))
        {
            var page = 1;
            int? totalPages = null;
            do
            {
                var start = FormatPayPalDate(window.From);
                var end = FormatPayPalDate(window.To);
                var path =
                    $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=all&page_size=500&page={page}&balance_affecting_records_only=N";

                var response = await SendAsync<PayPalSearchResponse>(HttpMethod.Get, path, null, null, cancellationToken);
                var details = response.TransactionDetails ?? new List<PayPalTransactionDetail>();
                foreach (var detail in details)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId == null)
                    {
                        continue;
                    }

                    results.Add(new PayPalReportedTransaction(
                        info.TransactionId,
                        info.InvoiceId,
                        info.CustomField,
                        info.PaypalReferenceId,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        info.TransactionAmount?.Value,
                        info.FeeAmount?.Value,
                        info.TransactionAmount?.CurrencyCode,
                        ParseTime(info.TransactionInitiationDate)));
                }

                totalPages = response.TotalPages;
                var morePages = totalPages.HasValue
                    ? page < totalPages.Value
                    : details.Count >= 500;
                page++;
                if (!morePages)
                {
                    break;
                }
            } while (true);
        }

        return results;
    }

    private PayPalCardRequest BuildCard(CardPaymentDetails? card, string? vaultId)
    {
        if (!string.IsNullOrEmpty(vaultId))
        {
            return new PayPalCardRequest
            {
                VaultId = vaultId,
                StoredCredential = new PayPalStoredCredential
                {
                    PaymentInitiator = "CUSTOMER",
                    PaymentType = "ONE_TIME",
                    Usage = "SUBSEQUENT"
                }
            };
        }

        if (card == null)
        {
            throw new OrderPaymentException("Card details are required.", 400, "PAYMENT_SOURCE_REQUIRED");
        }

        return new PayPalCardRequest
        {
            Name = card.Name,
            Number = DigitsOnly(card.Number),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToAddress(card.BillingAddress)
        };
    }

    private static PayPalVaultCard ToVaultCard(CardPaymentDetails card)
    {
        return new PayPalVaultCard
        {
            Name = card.Name,
            Number = DigitsOnly(card.Number),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToAddress(card.BillingAddress)
        };
    }

    private static PayPalAddress? ToAddress(CardBillingAddress? address)
    {
        if (address == null)
        {
            return new PayPalAddress
            {
                AddressLine1 = "123 Eshop Street",
                AdminArea2 = "San Jose",
                AdminArea1 = "CA",
                PostalCode = "95131",
                CountryCode = "US"
            };
        }

        return new PayPalAddress
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = string.IsNullOrWhiteSpace(address.CountryCode) ? "US" : address.CountryCode
        };
    }

    private static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());

    private static PayPalMoney Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = PayPalMoneyFormatter.Format(amount, currency)
    };

    private static PayPalAuthorization? FirstAuthorization(PayPalOrderResponse order) =>
        order.PurchaseUnits?.SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorization>()).FirstOrDefault();

    private static bool HasPayerActionLink(PayPalOrderResponse order) => HasPayerActionLink(order.Links);

    private static bool HasPayerActionLink(IEnumerable<PayPalLink>? links) =>
        links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true;

    private static AuthorizationSnapshot ToSnapshot(PayPalAuthorization auth)
    {
        if (string.IsNullOrEmpty(auth.Id))
        {
            throw new OrderPaymentException("PayPal authorization is missing an id.", 502, "PAYPAL_AUTHORIZATION_MISSING");
        }

        return new AuthorizationSnapshot(
            auth.Id,
            auth.Status ?? "CREATED",
            ParseTime(auth.ExpirationTime),
            ParseTime(auth.CreateTime));
    }

    private static CaptureResult ToCaptureResult(PayPalCapture capture, string fallbackCurrency)
    {
        if (string.IsNullOrEmpty(capture.Id))
        {
            throw new OrderPaymentException("PayPal did not return a capture id.", 502, "PAYPAL_CAPTURE_MISSING");
        }

        var breakdown = capture.SellerReceivableBreakdown;
        return new CaptureResult(
            capture.Id,
            capture.Status ?? "COMPLETED",
            PayPalMoneyFormatter.Parse(capture.Amount?.Value ?? breakdown?.GrossAmount?.Value),
            breakdown?.PaypalFee == null ? null : PayPalMoneyFormatter.Parse(breakdown.PaypalFee.Value),
            breakdown?.NetAmount == null ? null : PayPalMoneyFormatter.Parse(breakdown.NetAmount.Value),
            capture.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? fallbackCurrency);
    }

    private static DateTimeOffset? ParseTime(string? value)
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

    private static string FormatPayPalDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> SplitWindows(DateTimeOffset from, DateTimeOffset to)
    {
        var cursor = from;
        var maxWindow = TimeSpan.FromDays(31);
        while (true)
        {
            var windowEnd = cursor + maxWindow;
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

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, path, body, requestId, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw MapError(response.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new OrderPaymentException("PayPal returned an empty response body.", 502, "PAYPAL_EMPTY_RESPONSE");
        }

        var parsed = PayPalJson.Deserialize<T>(payload);
        if (parsed == null)
        {
            throw new OrderPaymentException("PayPal returned an unreadable response.", 502, "PAYPAL_INVALID_RESPONSE");
        }

        return parsed;
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, string? requestId, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        Exception? lastException = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrEmpty(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            if (body != null)
            {
                request.Content = PayPalJson.ToContent(body);
            }

            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    lastException = MapError(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                    response.Dispose();
                    response = null;
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (attempt < 2)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
            }
        }

        throw lastException ?? new OrderPaymentException("PayPal request failed.", 502, "PAYPAL_REQUEST_FAILED");
    }

    private OrderPaymentException MapError(HttpStatusCode statusCode, string payload)
    {
        var error = SafeDeserializeError(payload);
        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var description = error?.Details?.FirstOrDefault()?.Description;
        var name = error?.Name ?? statusCode.ToString();
        var message = error?.Message ?? "PayPal request failed.";
        if (!string.IsNullOrEmpty(description))
        {
            message = $"{message} {description}";
        }

        _logger.LogWarning("PayPal error {Status} {Name} {Issue} debugId={DebugId}", (int)statusCode, name, issue, error?.DebugId);

        var mappedStatus = statusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            (HttpStatusCode)422 => 422,
            _ => 502
        };

        if (string.Equals(issue, "ORDER_ALREADY_AUTHORIZED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "UNPROCESSABLE_ENTITY", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(issue, "AUTHORIZATION_ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            mappedStatus = 409;
        }

        if (string.Equals(issue, "AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(issue, "PREVIOUSLY_VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            return new OrderPaymentException(message, 422, "ALREADY_VOIDED");
        }

        return new OrderPaymentException(message, mappedStatus, issue ?? name);
    }

    private static PayPalError? SafeDeserializeError(string payload)
    {
        try
        {
            return PayPalJson.Deserialize<PayPalError>(payload);
        }
        catch
        {
            return null;
        }
    }
}
