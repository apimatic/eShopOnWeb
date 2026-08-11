using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

// ---- POST /api/orders ----

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }

    /// <summary>Maps to the domain address, defaulting to the app's placeholder when fields are omitted.</summary>
    public Address ToAddress() => new(
        string.IsNullOrWhiteSpace(Street) ? "123 Main St." : Street,
        string.IsNullOrWhiteSpace(City) ? "Kent" : City,
        string.IsNullOrWhiteSpace(State) ? "OH" : State,
        string.IsNullOrWhiteSpace(Country) ? "United States" : Country,
        string.IsNullOrWhiteSpace(ZipCode) ? "44240" : ZipCode);
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>The new order's id (top-level, as required to drive the flow end to end).</summary>
    public int OrderId { get; set; }

    public decimal Total { get; set; }
    public string Currency { get; set; } = default!;
    public string PaymentStatus { get; set; } = default!;
    public List<CreateOrderLineView> Items { get; set; } = new();
}

public class CreateOrderLineView
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = default!;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

// ---- POST /api/orders/{orderId}/pay ----

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Provide this or <see cref="SavedPaymentMethodId"/>, not both.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }

    /// <summary>When paying with a one-off card, also vault it for reuse.</summary>
    public bool SaveCard { get; set; }

    public PayInstruction ToInstruction() =>
        new(Card?.ToCardDetails(), SavedPaymentMethodId, SaveCard);
}

/// <summary>Response for pay/fulfil/cancel — the current payment state.</summary>
public class PaymentActionResponse
{
    public PaymentView Payment { get; set; } = default!;
}

// ---- POST /api/orders/{orderId}/refunds ----

public class RefundOrderRequest
{
    /// <summary>Amount to refund; omit for a full refund of what remains.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key; repeating it never refunds twice.</summary>
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse
{
    /// <summary>The refund's id (top-level, as required).</summary>
    public string RefundId { get; set; } = default!;

    public string Status { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = default!;
    public PaymentView Payment { get; set; } = default!;
}

// ---- GET /api/my-orders ----

public class MyOrderView
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string? PaymentStatus { get; set; }
    public PaymentView? Payment { get; set; }
    public List<CreateOrderLineView> Items { get; set; } = new();

    public static MyOrderView From(OrderWithPayment ow)
    {
        var order = ow.Order;
        return new MyOrderView
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            PaymentStatus = ow.Payment?.Status.ToString(),
            Payment = ow.Payment is null ? null : PaymentView.From(ow.Payment),
            Items = order.OrderItems.Select(i => new CreateOrderLineView
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };
    }
}

public class MyOrdersResponse
{
    public IReadOnlyList<MyOrderView> Orders { get; set; } = new List<MyOrderView>();
}
