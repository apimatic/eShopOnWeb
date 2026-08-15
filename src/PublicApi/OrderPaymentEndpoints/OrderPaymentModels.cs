using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentShared;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

// ----- Requests -----

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

public class CreateOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipTo { get; set; }
}

public class PayOrderRequest
{
    /// <summary>Set from the route, not the body.</summary>
    public int OrderId { get; set; }

    /// <summary>A one-off card, or null when paying with a saved card.</summary>
    public CardDto? Card { get; set; }

    /// <summary>A saved card id, or null when paying with a one-off card.</summary>
    public int? SavedPaymentMethodId { get; set; }

    public PayOrderCommand ToCommand()
    {
        var cardInput = Card != null ? CardMapping.ToInput(Card) : null;
        return new PayOrderCommand(cardInput, SavedPaymentMethodId);
    }
}

public class FulfilOrderRequest
{
    public FulfilOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; }
}

public class CancelOrderRequest
{
    public CancelOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; }
}

public class RefundOrderRequest
{
    /// <summary>Set from the route, not the body.</summary>
    public int OrderId { get; set; }

    /// <summary>Amount to refund; null refunds the whole remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key — required. Repeating it does not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

// ----- Responses -----

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>The full payment view of an order, returned by pay/fulfil/cancel and my-orders.</summary>
public class OrderPaymentResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public decimal Total { get; set; }

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public int? SavedPaymentMethodId { get; set; }

    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }

    public List<OrderItemDto> Items { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderPaymentResponse From(Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        PaymentStatus = order.PaymentStatus.ToString(),
        Currency = order.PaymentCurrency,
        Total = order.Total(),
        PayPalOrderId = order.PayPalOrderId,
        AuthorizationId = order.PayPalAuthorizationId,
        CaptureId = order.PayPalCaptureId,
        AuthorizationExpiresAt = order.AuthorizationExpiresAt,
        SavedPaymentMethodId = order.SavedPaymentMethodId,
        CapturedAmount = order.CapturedAmount,
        PayPalFee = order.PayPalFee,
        NetAmount = order.NetAmount,
        TotalRefunded = order.TotalRefunded(),
        RefundableRemaining = order.RefundableRemaining(),
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Refunds = order.Refunds.Select(r => new RefundDto
        {
            RefundId = r.Id,
            PayPalRefundId = r.PayPalRefundId,
            Amount = r.Amount,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList()
    };
}

public class MyOrdersResponse
{
    public List<OrderPaymentResponse> Orders { get; set; } = new();
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string OrderPaymentStatus { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
}
