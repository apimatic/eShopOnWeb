using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Place an order from catalog items. The caller's identity comes from the token.</summary>
public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>Identifier of the order that was placed.</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

/// <summary>Result of an operator dispatch/cancel action on an order.</summary>
public class OrderStatusResponse : BaseResponse
{
    public OrderStatusResponse(Guid correlationId) : base(correlationId) { }
    public OrderStatusResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
