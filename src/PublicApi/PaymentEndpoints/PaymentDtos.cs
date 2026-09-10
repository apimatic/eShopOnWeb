using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Requests --------------------------------------------------------------------------------------

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipTo { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>Pay for an order with either a one-off card or one of the shopper's saved cards.</summary>
public class PayOrderRequest
{
    public CardDto? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;   // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class RefundOrderRequest
{
    /// <summary>Omit for a full refund; set for a partial refund.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. May also be sent as the <c>Idempotency-Key</c> header.</summary>
    public string? IdempotencyKey { get; set; }
}

public class SavePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();
}

// ---- Responses (create responses carry their identifier as a top-level field) ----------------------

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = "AwaitingPayment";
}
