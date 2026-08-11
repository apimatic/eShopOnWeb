using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ------------------------------------------------------------------ request DTOs

/// <summary>A billing address for a card. Optional; PayPal uses it for risk/AVS checks.</summary>
public class BillingAddressDto
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}

/// <summary>
/// Raw card details for a one-off payment or for saving a card. These are forwarded to PayPal
/// and never stored by this application.
/// </summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number,
        Expiry,
        SecurityCode,
        CardholderName,
        BillingAddress is null
            ? null
            : new CardBillingAddress(
                BillingAddress.AddressLine1,
                BillingAddress.AddressLine2,
                BillingAddress.City,
                BillingAddress.State,
                BillingAddress.PostalCode,
                BillingAddress.CountryCode));
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Body of POST /api/orders.</summary>
public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
}

/// <summary>Body of POST /api/orders/{orderId}/pay — a one-off card or a saved card id.</summary>
public class PayOrderRequest
{
    public CardDto? Card { get; set; }
    public int? SavedCardId { get; set; }
}

/// <summary>Body of POST /api/orders/{orderId}/refunds.</summary>
public class RefundRequest
{
    /// <summary>Amount to refund; omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Body of POST /api/payment-methods.</summary>
public class CreatePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();
}
