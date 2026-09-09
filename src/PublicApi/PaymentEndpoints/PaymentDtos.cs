using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ----- shared card + address input -----

/// <summary>Raw card details for a one-off payment or to save. Never stored or logged by this app.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;   // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public GatewayCard ToGatewayCard() => new(
        Number: Number,
        Expiry: Expiry,
        SecurityCode: SecurityCode,
        CardholderName: CardholderName,
        BillingAddress: BillingAddress?.ToGatewayBillingAddress());
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }   // state / province
    public string? AdminArea2 { get; set; }   // city
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }  // ISO-3166 alpha-2

    public GatewayBillingAddress ToGatewayBillingAddress() =>
        new(AddressLine1, AddressLine2, AdminArea1, AdminArea2, PostalCode, CountryCode);
}

// ----- Flow 1 requests -----

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public ShippingAddressInput ToInput() => new(Street, City, State, Country, ZipCode);
}

public class PlaceOrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderRequest
{
    public List<PlaceOrderItemDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class PayOrderRequest
{
    /// <summary>Route order id (set by the endpoint from the URL, not the body).</summary>
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with. Provide this OR <see cref="Card"/>.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

/// <summary>Carries only the route order id, for operator fulfil/cancel actions with no body.</summary>
public class OrderActionRequest
{
    public int OrderId { get; set; }
}

public class RefundOrderRequest
{
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class MyOrdersRequest
{
}

public class ReconciliationRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
}

public class ListPaymentMethodsRequest
{
}

public class DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; set; }
}

public class RefundRequestDto
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining refundable amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key: repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

// ----- Flow 2 requests -----

public class SavePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();
    public string? Alias { get; set; }
}
