using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Implements the order authorize / capture / void / reauthorize / refund flow against PayPal's
/// Checkout Orders v2 and Payments v2 APIs, exactly as described by the specs in <c>api-specs/</c>.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private readonly PayPalApiClient _client;

    public PayPalPaymentGateway(PayPalApiClient client) => _client = client;

    private static (string Name, string Value)[] Headers(string requestId, bool representation = true)
    {
        var headers = new List<(string, string)> { ("PayPal-Request-Id", requestId) };
        if (representation)
        {
            headers.Add(("Prefer", "return=representation"));
        }
        return headers.ToArray();
    }

    internal static string FormatValue(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static Money? ToMoney(MoneyModel? m)
    {
        if (m?.Value == null || m.CurrencyCode == null) return null;
        return decimal.TryParse(m.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? new Money(m.CurrencyCode, v)
            : null;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeCommand command, CancellationToken cancellationToken)
    {
        var card = command.Instrument.IsSavedCard
            ? new CardRequestModel { VaultId = command.Instrument.VaultId }
            : MapCard(command.Instrument.Card!);

        var createRequest = new CreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = "default",
                    InvoiceId = command.InvoiceId,
                    CustomId = command.CustomId ?? command.InvoiceId,
                    Description = command.Description,
                    Amount = new MoneyModel
                    {
                        CurrencyCode = command.Amount.CurrencyCode,
                        Value = FormatValue(command.Amount.Value)
                    }
                }
            },
            PaymentSource = new PaymentSourceRequest { Card = card }
        };

        var order = await _client.SendAsync<OrderResponse>(HttpMethod.Post, "/v2/checkout/orders",
            createRequest, Headers(command.IdempotencyKey), cancellationToken);

        GuardNoChallenge(order);

        // When a payment source is supplied on create, PayPal may authorize in the same call.
        var authorization = ExtractAuthorization(order);
        var payPalOrderId = order.Id
            ?? throw new PayPalApiException("PayPal did not return an order id on order creation.");

        if (authorization == null)
        {
            var status = order.Status ?? "UNKNOWN";
            if (status is "APPROVED" or "CREATED" or "SAVED")
            {
                var authorized = await _client.SendAsync<OrderResponse>(HttpMethod.Post,
                    $"/v2/checkout/orders/{payPalOrderId}/authorize", body: null,
                    Headers(command.IdempotencyKey + "-authorize"), cancellationToken);
                GuardNoChallenge(authorized);
                authorization = ExtractAuthorization(authorized);
            }
        }

        if (authorization?.Id == null)
        {
            throw new PayPalApiException(
                $"PayPal order {payPalOrderId} did not yield an authorization (order status '{order.Status}').");
        }

        return new AuthorizationResult(payPalOrderId, authorization.Id, authorization.Status ?? "CREATED",
            ParseTime(authorization.ExpirationTime));
    }

    public async Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        var auth = await _client.SendAsync<AuthorizationResponse>(HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}", body: null, headers: null, cancellationToken);
        return new AuthorizationResult(string.Empty, auth.Id ?? authorizationId, auth.Status ?? "UNKNOWN",
            ParseTime(auth.ExpirationTime));
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, Money amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var body = new ReauthorizeRequestModel
        {
            Amount = new MoneyModel { CurrencyCode = amount.CurrencyCode, Value = FormatValue(amount.Value) }
        };
        var auth = await _client.SendAsync<AuthorizationResponse>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body,
            Headers(idempotencyKey), cancellationToken);
        return new AuthorizationResult(string.Empty, auth.Id ?? authorizationId, auth.Status ?? "CREATED",
            ParseTime(auth.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, Money amount, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var body = new CaptureRequestModel
        {
            Amount = new MoneyModel { CurrencyCode = amount.CurrencyCode, Value = FormatValue(amount.Value) },
            FinalCapture = true
        };
        var capture = await _client.SendAsync<CaptureResponse>(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture", body,
            Headers(idempotencyKey), cancellationToken);

        if (capture.Id == null)
        {
            throw new PayPalApiException("PayPal capture response did not contain a capture id.");
        }

        var breakdown = capture.SellerReceivableBreakdown;
        var gross = ToMoney(breakdown?.GrossAmount) ?? ToMoney(capture.Amount)
            ?? new Money(amount.CurrencyCode, amount.Value);
        return new CaptureResult(capture.Id, capture.Status ?? "COMPLETED", gross,
            ToMoney(breakdown?.PaypalFee), ToMoney(breakdown?.NetAmount));
    }

    public Task VoidAsync(string authorizationId, CancellationToken cancellationToken)
        => _client.SendNoContentAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void", body: null, headers: null, cancellationToken);

    public async Task<RefundResult> RefundAsync(string captureId, Money? amount, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Null amount => full refund (empty body). A value => partial refund of that amount.
        var body = new RefundRequestModel
        {
            Amount = amount == null
                ? null
                : new MoneyModel { CurrencyCode = amount.CurrencyCode, Value = FormatValue(amount.Value) }
        };
        var refund = await _client.SendAsync<RefundResponse>(HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund", body, Headers(idempotencyKey), cancellationToken);

        if (refund.Id == null)
        {
            throw new PayPalApiException("PayPal refund response did not contain a refund id.");
        }

        var refunded = ToMoney(refund.Amount) ?? amount ?? new Money(_client.Currency, 0m);
        return new RefundResult(refund.Id, refund.Status ?? "COMPLETED", refunded);
    }

    private static CardRequestModel MapCard(CardDetails card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = card.BillingAddress == null ? null : new BillingAddressModel
        {
            AddressLine1 = card.BillingAddress.AddressLine1,
            AddressLine2 = card.BillingAddress.AddressLine2,
            AdminArea2 = card.BillingAddress.AdminArea2,
            AdminArea1 = card.BillingAddress.AdminArea1,
            PostalCode = card.BillingAddress.PostalCode,
            CountryCode = card.BillingAddress.CountryCode
        }
    };

    private static AuthorizationResponse? ExtractAuthorization(OrderResponse order)
        => order.PurchaseUnits?
            .Select(pu => pu.Payments?.Authorizations?.FirstOrDefault())
            .FirstOrDefault(a => a?.Id != null);

    private static void GuardNoChallenge(OrderResponse order)
    {
        var status = order.Status;
        var payerAction = order.Links?.Any(l =>
            string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false;
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) || payerAction)
        {
            throw new PayPalChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (3-D Secure / " +
                "payer-action challenge). This integration does not perform a browser approval round-trip.");
        }
    }

    private static DateTimeOffset? ParseTime(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt) ? dt : null;
}
