using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Implements the domain <see cref="IPayPalGateway"/> by mapping the app's payment operations onto
/// the PayPal REST calls exposed by <see cref="PayPalApiClient"/>. Card charges use PayPal's
/// single-step create-with-card flow (intent CAPTURE); if PayPal returns an approved-but-uncaptured
/// order it is captured explicitly as a fallback.
/// </summary>
internal sealed class PayPalGateway : IPayPalGateway
{
    private readonly PayPalApiClient _client;

    public PayPalGateway(PayPalApiClient client)
    {
        _client = client;
    }

    public async Task<CapturedPayment> CaptureCardPaymentAsync(
        decimal amount,
        string currencyCode,
        CardPaymentSource source,
        string idempotencyKey,
        string orderReference,
        CancellationToken cancellationToken = default)
    {
        var request = new OrderRequest
        {
            Intent = "CAPTURE",
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = orderReference,
                    CustomId = orderReference,
                    Amount = new AmountRequest
                    {
                        CurrencyCode = currencyCode,
                        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
                    }
                }
            },
            PaymentSource = new PaymentSourceRequest { Card = MapCard(source) }
        };

        var order = await _client.CreateOrderAsync(request, idempotencyKey, cancellationToken);

        // The single-step card flow normally returns COMPLETED. If PayPal returns APPROVED (authorized
        // but not captured), capture it explicitly using the same idempotency key.
        if (string.Equals(order.Status, "APPROVED", System.StringComparison.OrdinalIgnoreCase))
        {
            order = await _client.CaptureOrderAsync(order.Id!, idempotencyKey, cancellationToken);
        }

        var capture = order.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Captures?
            .FirstOrDefault();

        if (order.Id is null || capture?.Id is null)
        {
            throw new PayPalApiException(
                $"PayPal did not return a capture for the payment (order status: {order.Status ?? "unknown"}).");
        }

        return new CapturedPayment(order.Id, capture.Id, capture.Status ?? order.Status ?? "UNKNOWN");
    }

    public async Task<RefundOutcome> RefundCaptureAsync(string captureId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var refund = await _client.RefundCaptureAsync(captureId, idempotencyKey, cancellationToken);
        if (refund.Id is null)
        {
            throw new PayPalApiException("PayPal did not return a refund id.");
        }
        return new RefundOutcome(refund.Id, refund.Status ?? "UNKNOWN");
    }

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, string customerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var request = new VaultPaymentTokenRequest
        {
            PaymentSource = new VaultPaymentSourceRequest { Card = MapRawCard(card) },
            Customer = string.IsNullOrEmpty(customerId) ? null : new VaultCustomer { Id = customerId }
        };

        var response = await _client.CreatePaymentTokenAsync(request, idempotencyKey, cancellationToken);
        if (response.Id is null)
        {
            throw new PayPalApiException("PayPal did not return a vault token id.");
        }

        var respCard = response.PaymentSource?.Card;
        return new VaultedCard(
            response.Id,
            respCard?.Brand,
            respCard?.LastDigits,
            respCard?.Name ?? card.CardholderName,
            respCard?.Expiry ?? card.Expiry);
    }

    public Task RemoveVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
        => _client.DeletePaymentTokenAsync(vaultId, cancellationToken);

    private static CardRequest MapCard(CardPaymentSource source) => source switch
    {
        VaultedCardSource vaulted => new CardRequest { VaultId = vaulted.VaultId },
        RawCardSource raw => MapRawCard(raw.Card),
        _ => throw new PaymentInputException("Unsupported payment source.")
    };

    private static CardRequest MapRawCard(CardDetails card) => new()
    {
        Number = CardValidation.NormalizeNumber(card.Number),
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = MapAddress(card.BillingAddress)
    };

    private static AddressRequest? MapAddress(BillingAddressDetails? address)
    {
        if (address is null)
        {
            return null;
        }
        return new AddressRequest
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }
}
