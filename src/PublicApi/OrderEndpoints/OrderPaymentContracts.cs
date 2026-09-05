using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Places an order from catalog items.</summary>
public class PlaceOrderRequest : ShopperRequest
{
    public List<OrderLineRequest> Items { get; set; } = new List<OrderLineRequest>();
    public ShippingAddressRequest? ShipTo { get; set; }
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>The identifier of the order that was placed.</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderLineDto> Items { get; set; } = new List<OrderLineDto>();
    public string? Note { get; set; }
}

/// <summary>
/// Pays for an order. Either <see cref="Card"/> for a one-off payment or <see cref="PaymentMethodId"/>
/// for one of the shopper's saved cards; not both.
/// </summary>
public class PayOrderRequest : ShopperRequest
{
    public int OrderId { get; set; }
    public PaymentCardDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;

    /// <summary>Set when the order had already been paid for, so nothing new was held.</summary>
    public bool AlreadyRecorded { get; set; }

    public PaymentDto? Payment { get; set; }
    public string? Note { get; set; }
}

/// <summary>Fulfils an order named in the route. An operator action.</summary>
public class FulfilOrderRequest : BaseRequest
{
    public FulfilOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public bool AlreadyRecorded { get; set; }

    /// <summary>Set when a hold that had gone stale was renewed so the order could be fulfilled.</summary>
    public bool RenewedHold { get; set; }

    public PaymentDto? Payment { get; set; }
    public string? Note { get; set; }
}

/// <summary>Cancels an order named in the route. An operator action.</summary>
public class CancelOrderRequest : BaseRequest
{
    public CancelOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public bool AlreadyRecorded { get; set; }
    public PaymentDto? Payment { get; set; }
    public string? Note { get; set; }
}

/// <summary>Returns a fulfilled order's money, in full or in part.</summary>
public class RefundOrderRequest : ShopperRequest
{
    public int OrderId { get; set; }

    /// <summary>
    /// Caller-supplied key. Repeating a refund under the same key replays the refund already made
    /// instead of returning money twice; a different key is a separate, legitimate partial return.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Omitted or null returns everything that can still be given back.</summary>
    public decimal? Amount { get; set; }

    public string? NoteToShopper { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>The identifier of the refund this request produced.</summary>
    public int RefundId { get; set; }

    public int OrderId { get; set; }
    public bool AlreadyRecorded { get; set; }
    public RefundDto? Refund { get; set; }
    public PaymentDto? Payment { get; set; }
    public decimal RefundableAmount { get; set; }
}

/// <summary>The caller's own orders. No filter: identity comes from the token.</summary>
public class MyOrdersRequest : ShopperRequest
{
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }

    public List<PlacedOrderDto> Orders { get; set; } = new List<PlacedOrderDto>();
}
