using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>A requested order line.</summary>
public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Optional shipping address for a placed order.</summary>
public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToDomain() => new(Street, City, State, Country, ZipCode);
}

/// <summary>Body of POST /api/orders.</summary>
public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

/// <summary>Body of POST /api/orders/{orderId}/pay. Supply exactly one funding source.</summary>
public class PayOrderRequest
{
    /// <summary>Set from the route, not the request body.</summary>
    public int OrderId { get; set; }
    public CardDto? Card { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

/// <summary>Body of POST /api/orders/{orderId}/refunds.</summary>
public class RefundOrderRequest
{
    /// <summary>Set from the route, not the request body.</summary>
    public int OrderId { get; set; }

    /// <summary>Amount to refund; omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating it never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Body of POST /api/payment-methods.</summary>
public class SavePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();
}
