using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Implements the authorize / capture / reauthorize / void / refund flow against the PayPal Orders v2
/// and Payments v2 APIs, exactly as described by their OpenAPI specs.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private readonly PayPalApiClient _client;

    public PayPalPaymentGateway(PayPalApiClient client)
    {
        _client = client;
    }

    public async Task<GatewayAuthorization> AuthorizeOrderAsync(AuthorizeGatewayRequest request, CancellationToken cancellationToken = default)
    {
        var card = new CardRequest();
        if (request.Card is not null)
        {
            var c = request.Card;
            card.Name = c.CardholderName;
            card.Number = c.Number;
            card.Expiry = c.Expiry;
            card.SecurityCode = c.SecurityCode;
            card.BillingAddress = ToAddress(c);
        }
        else if (!string.IsNullOrEmpty(request.VaultId))
        {
            card.VaultId = request.VaultId;
            // Customer-initiated reuse of a stored card.
            card.StoredCredential = new CardStoredCredential
            {
                PaymentInitiator = "CUSTOMER",
                PaymentType = "UNSCHEDULED",
                Usage = "SUBSEQUENT",
            };
        }
        else
        {
            throw new PaymentGatewayException("An authorization needs either card details or a saved card.", 400, "MISSING_PAYMENT_SOURCE");
        }

        var body = new CreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new()
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = "default",
                    Amount = Money(request.Amount, request.Currency),
                    InvoiceId = request.ReferenceId,
                    CustomId = request.CustomId,
                },
            },
            PaymentSource = new PaymentSourceRequest { Card = card },
        };

        var headers = new PayPalRequestHeaders
        {
            RequestId = request.IdempotencyKey,
            Prefer = "return=representation",
        };

        var order = await _client.SendAsync<OrderResponse>(
            HttpMethod.Post, "/v2/checkout/orders", body, headers, cancellationToken);

        return MapOrderToAuthorization(order);
    }

    public async Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new CaptureRequest
        {
            Amount = Money(amount, currency),
            FinalCapture = true,
        };
        var headers = new PayPalRequestHeaders
        {
            RequestId = idempotencyKey,
            Prefer = "return=representation",
        };

        var capture = await _client.SendAsync<CaptureResponse>(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", body, headers, cancellationToken);

        if (capture?.Id is null)
        {
            throw new PaymentGatewayException("PayPal capture response did not contain a capture id.", 502, "CAPTURE_ID_MISSING");
        }

        var breakdown = capture.SellerReceivableBreakdown;
        var gross = PayPalMoney.Parse(breakdown?.GrossAmount?.Value ?? capture.Amount?.Value);
        var fee = PayPalMoney.Parse(breakdown?.PaypalFee?.Value);
        var net = breakdown?.NetAmount?.Value is { } netValue ? PayPalMoney.Parse(netValue) : gross - fee;
        var captureCurrency = breakdown?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode ?? currency;

        return new GatewayCapture(capture.Id, capture.Status ?? "UNKNOWN", gross, fee, net, captureCurrency);
    }

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequest { Amount = Money(amount, currency) };
        var headers = new PayPalRequestHeaders
        {
            RequestId = idempotencyKey,
            Prefer = "return=representation",
        };

        var auth = await _client.SendAsync<AuthorizationResponse>(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, headers, cancellationToken);

        return MapAuthorization(auth);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        await _client.SendNoContentAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            body: null,
            headers: new PayPalRequestHeaders { Prefer = "return=minimal" },
            cancellationToken);
    }

    public async Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = Money(amount.Value, currency) }
            : new RefundRequest(); // empty body => full refund
        var headers = new PayPalRequestHeaders
        {
            RequestId = idempotencyKey,
            Prefer = "return=representation",
        };

        var refund = await _client.SendAsync<RefundResponse>(
            HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, headers, cancellationToken);

        if (refund?.Id is null)
        {
            throw new PaymentGatewayException("PayPal refund response did not contain a refund id.", 502, "REFUND_ID_MISSING");
        }

        var refundedAmount = refund.Amount?.Value is { } v ? PayPalMoney.Parse(v) : (amount ?? 0m);
        var refundCurrency = refund.Amount?.CurrencyCode ?? currency;
        return new GatewayRefund(refund.Id, refund.Status ?? "UNKNOWN", refundedAmount, refundCurrency);
    }

    public async Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var auth = await _client.SendAsync<AuthorizationResponse>(
            HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", null, null, cancellationToken);
        return MapAuthorization(auth);
    }

    private static GatewayAuthorization MapOrderToAuthorization(OrderResponse? order)
    {
        if (order?.Id is null)
        {
            throw new PaymentGatewayException("PayPal order response did not contain an order id.", 502, "ORDER_ID_MISSING");
        }

        var requiresPayerAction =
            string.Equals(order.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || (order.Links?.Any(l =>
                    string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase)) ?? false);

        var authorization = order.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

        return new GatewayAuthorization(
            order.Id,
            order.Status ?? "UNKNOWN",
            authorization?.Id,
            authorization?.Status,
            ParseDate(authorization?.ExpirationTime),
            requiresPayerAction);
    }

    private static GatewayAuthorization MapAuthorization(AuthorizationResponse? auth)
    {
        if (auth?.Id is null)
        {
            throw new PaymentGatewayException("PayPal authorization response did not contain an authorization id.", 502, "AUTH_ID_MISSING");
        }

        return new GatewayAuthorization(
            PayPalOrderId: string.Empty,
            OrderStatus: "UNKNOWN",
            AuthorizationId: auth.Id,
            AuthorizationStatus: auth.Status,
            ExpiresAt: ParseDate(auth.ExpirationTime),
            RequiresPayerAction: false);
    }

    private static Money Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = PayPalMoney.Format(amount, currency),
    };

    private static AddressPortable ToAddress(CardDetails c) => new()
    {
        AddressLine1 = c.Line1,
        AddressLine2 = c.Line2,
        AdminArea2 = c.City,
        AdminArea1 = c.State,
        PostalCode = c.PostalCode,
        CountryCode = c.CountryCode,
    };

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
