using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public AddressRequest? ShippingAddress { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class OrderActionRequest : BaseRequest
{
    public int OrderId { get; init; }

    public OrderActionRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; init; }
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    public RefundOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class MyOrdersRequest : BaseRequest
{
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}
