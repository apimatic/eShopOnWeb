using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly HttpClient _httpClient;
    private readonly IPayPalAccessTokenProvider _tokenProvider;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(
        HttpClient httpClient,
        IPayPalAccessTokenProvider tokenProvider,
        ILogger<PayPalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public Task<AuthorizationResult> AuthorizeCardAsync(
        string invoiceId,
        string customId,
        MoneyAmount amount,
        CardPaymentSource card,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSource
        {
            Card = ToCardRequest(card)
        };
        return AuthorizeAsync(invoiceId, customId, amount, paymentSource, idempotencyKey, cancellationToken);
    }

    public Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        string invoiceId,
        string customId,
        MoneyAmount amount,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = new PayPalPaymentSource
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
        return AuthorizeAsync(invoiceId, customId, amount, paymentSource, idempotencyKey, cancellationToken);
    }

    public async Task<AuthorizationDetails> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await SendAsync<PayPalAuthorization>(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            idempotencyKey: null,
            cancellationToken);

        return ToAuthorizationDetails(authorization);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        MoneyAmount amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalReauthorizeRequest
        {
            Amount = ToMoney(amount)
        };

        var authorization = await SendAsync<PayPalAuthorization>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            request,
            idempotencyKey,
            cancellationToken);

        return new AuthorizationResult(
            PayPalOrderId: string.Empty,
            PayPalOrderStatus: string.Empty,
            AuthorizationId: authorization.Id ?? throw Missing("reauthorize", "id"),
            AuthorizationStatus: authorization.Status ?? string.Empty,
            ExpirationTime: ParseTimestamp(authorization.ExpirationTime),
            Amount: ToMoneyAmount(authorization.Amount) ?? amount);
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId,
        MoneyAmount amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalCaptureRequest
        {
            Amount = ToMoney(amount),
            FinalCapture = true
        };

        var capture = await SendAsync<PayPalCapture>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            request,
            idempotencyKey,
            cancellationToken);

        return ToCaptureResult(capture, amount);
    }

    public async Task VoidAuthorizationAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        await SendAsync<PayPalAuthorization>(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            body: null,
            idempotencyKey,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<RefundGatewayResult> RefundCaptureAsync(
        string captureId,
        MoneyAmount amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalRefundRequest
        {
            Amount = ToMoney(amount)
        };

        var refund = await SendAsync<PayPalRefund>(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            request,
            idempotencyKey,
            cancellationToken);

        return new RefundGatewayResult(
            refund.Id ?? throw Missing("refund", "id"),
            refund.Status ?? string.Empty,
            ToMoneyAmount(refund.Amount) ?? amount);
    }

    public async Task<VaultedCardResult> VaultCardAsync(
        string merchantCustomerId,
        CardPaymentSource card,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new PayPalPaymentTokenRequest
        {
            Customer = new PayPalCustomer { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new PayPalPaymentSource { Card = ToCardRequest(card) }
        };

        var token = await SendAsync<PayPalPaymentTokenResponse>(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            request,
            idempotencyKey,
            cancellationToken);

        EnsureNoPayerAction(token.Status, token.Links, "saving a card");

        var cardResponse = token.PaymentSource?.Card;
        return new VaultedCardResult(
            token.Id ?? throw Missing("payment-token", "id"),
            cardResponse?.LastDigits,
            cardResponse?.Brand,
            cardResponse?.Expiry,
            cardResponse?.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{vaultId}",
            body: null,
            idempotencyKey: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<TransactionSearchResult> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var all = new List<GatewayTransaction>();
        DateTimeOffset? lastRefreshed = null;
        var page = 1;
        int? totalPages = null;

        do
        {
            var start = FormatReportingDate(from);
            var end = FormatReportingDate(to);
            var path =
                $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(start)}&end_date={Uri.EscapeDataString(end)}&fields=transaction_info&page_size=500&page={page}&balance_affecting_records_only=N";

            var response = await SendAsync<PayPalSearchResponse>(
                HttpMethod.Get,
                path,
                body: null,
                idempotencyKey: null,
                cancellationToken);

            lastRefreshed = ParseTimestamp(response.LastRefreshedDatetime) ?? lastRefreshed;
            totalPages = response.TotalPages ?? totalPages;

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }

                    all.Add(new GatewayTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.CustomField,
                        info.InvoiceId,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        ParseTimestamp(info.TransactionInitiationDate),
                        ToMoneyAmount(info.TransactionAmount),
                        ToMoneyAmount(info.FeeAmount)));
                }
            }

            page++;
        } while (totalPages is > 0 && page <= totalPages.Value);

        return new TransactionSearchResult(all, lastRefreshed);
    }

    private async Task<AuthorizationResult> AuthorizeAsync(
        string invoiceId,
        string customId,
        MoneyAmount amount,
        PayPalPaymentSource paymentSource,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var createRequest = new PayPalOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PayPalPurchaseUnitRequest>
            {
                new()
                {
                    Amount = new PayPalAmountWithBreakdown
                    {
                        CurrencyCode = amount.Currency,
                        Value = PayPalMoneyFormat.Format(amount.Value, amount.Currency)
                    },
                    CustomId = customId,
                    InvoiceId = invoiceId
                }
            },
            PaymentSource = paymentSource
        };

        var order = await SendAsync<PayPalOrder>(
            HttpMethod.Post,
            "/v2/checkout/orders",
            createRequest,
            idempotencyKey,
            cancellationToken);

        EnsureNoPayerAction(order.Status, order.Links, "paying for an order");

        var authorization = FirstAuthorization(order);
        if (authorization is null)
        {
            order = await SendAsync<PayPalOrder>(
                HttpMethod.Post,
                $"/v2/checkout/orders/{order.Id}/authorize",
                body: new { },
                idempotencyKey: idempotencyKey + "-authorize",
                cancellationToken);

            EnsureNoPayerAction(order.Status, order.Links, "paying for an order");
            authorization = FirstAuthorization(order);
        }

        if (authorization?.Id is null)
        {
            throw new PaymentException(
                "PayPal authorized the order but did not return an authorization id.",
                502);
        }

        return new AuthorizationResult(
            order.Id ?? throw Missing("order", "id"),
            order.Status ?? string.Empty,
            authorization.Id,
            authorization.Status ?? string.Empty,
            ParseTimestamp(authorization.ExpirationTime),
            ToMoneyAmount(authorization.Amount) ?? amount);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? idempotencyKey,
        CancellationToken cancellationToken,
        bool allowNoContent = false)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = new StringContent(PayPalJson.Serialize(body), Utf8NoBom, "application/json");
        }

        _logger.LogInformation("PayPal {Method} {Path}", method.Method, SanitizePath(path));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || (allowNoContent && string.IsNullOrWhiteSpace(payload)))
        {
            if (allowNoContent)
            {
                return default!;
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw MapError((int)response.StatusCode, payload);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default!;
        }

        var parsed = PayPalJson.Deserialize<T>(payload);
        if (parsed is null)
        {
            throw new PaymentException("PayPal returned an empty or unreadable response.", 502);
        }

        return parsed;
    }

    private static PaymentException MapError(int statusCode, string payload)
    {
        var error = SafeDeserializeError(payload);
        var issue = error?.Details?.FirstOrDefault()?.Issue;
        var description = error?.Details?.FirstOrDefault()?.Description;
        var name = error?.Name;
        var message = error?.Message;

        var mappedStatus = statusCode switch
        {
            400 or 404 or 409 or 422 => statusCode,
            401 or 403 => 502,
            _ => statusCode >= 500 ? 502 : 400
        };

        var text = string.Join(" ", new[]
        {
            name,
            message,
            issue is null ? null : $"({issue})",
            description
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        if (string.IsNullOrWhiteSpace(text))
        {
            text = $"PayPal request failed with HTTP {statusCode}.";
        }

        if (!string.IsNullOrEmpty(error?.DebugId))
        {
            text += $" PayPal debug_id {error.DebugId}.";
        }

        return new PaymentException(text, mappedStatus, issue ?? name);
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

    private static void EnsureNoPayerAction(string? status, IEnumerable<PayPalLink>? links, string action)
    {
        var hasPayerActionLink = links?.Any(l =>
            string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true;

        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || hasPayerActionLink)
        {
            throw new PayerActionRequiredException(
                $"PayPal required a shopper to approve in a browser while {action}. This integration does not perform a browser round-trip.");
        }
    }

    private static PayPalAuthorization? FirstAuthorization(PayPalOrder order)
    {
        return order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorization>())
            .FirstOrDefault(a => !string.IsNullOrEmpty(a.Id));
    }

    private static PayPalCardRequest ToCardRequest(CardPaymentSource card)
    {
        return new PayPalCardRequest
        {
            Name = card.Name,
            Number = new string(card.Number.Where(char.IsDigit).ToArray()),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = card.Address is null
                ? null
                : new PayPalAddress
                {
                    AddressLine1 = card.Address.AddressLine1,
                    AddressLine2 = card.Address.AddressLine2,
                    AdminArea2 = card.Address.City,
                    AdminArea1 = card.Address.State,
                    PostalCode = card.Address.PostalCode,
                    CountryCode = card.Address.CountryCode
                }
        };
    }

    private static PayPalMoney ToMoney(MoneyAmount amount)
    {
        return new PayPalMoney
        {
            CurrencyCode = amount.Currency,
            Value = PayPalMoneyFormat.Format(amount.Value, amount.Currency)
        };
    }

    private static MoneyAmount? ToMoneyAmount(PayPalMoney? money)
    {
        if (money?.Value is null || money.CurrencyCode is null)
        {
            return null;
        }

        return new MoneyAmount(PayPalMoneyFormat.Parse(money.Value), money.CurrencyCode);
    }

    private static AuthorizationDetails ToAuthorizationDetails(PayPalAuthorization authorization)
    {
        return new AuthorizationDetails(
            authorization.Id ?? throw Missing("authorization", "id"),
            authorization.Status ?? string.Empty,
            ParseTimestamp(authorization.ExpirationTime),
            ToMoneyAmount(authorization.Amount));
    }

    private static CaptureResult ToCaptureResult(PayPalCapture capture, MoneyAmount fallback)
    {
        var captured = ToMoneyAmount(capture.Amount)
                       ?? ToMoneyAmount(capture.SellerReceivableBreakdown?.GrossAmount)
                       ?? fallback;

        return new CaptureResult(
            capture.Id ?? throw Missing("capture", "id"),
            capture.Status ?? string.Empty,
            captured,
            ToMoneyAmount(capture.SellerReceivableBreakdown?.PaypalFee),
            ToMoneyAmount(capture.SellerReceivableBreakdown?.NetAmount));
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
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

    private static string FormatReportingDate(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static PaymentException Missing(string resource, string field)
    {
        return new PaymentException($"PayPal {resource} response was missing '{field}'.", 502);
    }

    private static string SanitizePath(string path)
    {
        var query = path.IndexOf('?', StringComparison.Ordinal);
        return query < 0 ? path : path[..query];
    }
}
