using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>A shopper-facing view of an order and its payment state.</summary>
public class OrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public System.DateTimeOffset OrderDate { get; set; }
    public List<OrderLineResponse> Items { get; set; } = new();
    public PaymentResponse? Payment { get; set; }

    public static OrderResponse From(Order order)
    {
        var response = new OrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            OrderDate = order.OrderDate,
            Currency = order.Payment?.Currency,
            Items = order.OrderItems.Select(i => new OrderLineResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };

        if (order.Payment is Payment payment)
        {
            response.Payment = PaymentResponse.From(payment);
        }

        return response;
    }
}

public class OrderLineResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>The PayPal-owned payment state carried by an order (ids and statuses for hold, capture, refunds).</summary>
public class PaymentResponse
{
    public string Provider { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string? AuthorizationStatus { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();

    public static PaymentResponse From(Payment payment) => new()
    {
        Provider = payment.Provider,
        Currency = payment.Currency,
        Amount = payment.Amount,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        RefundedAmount = payment.RefundedAmount,
        RefundableRemaining = payment.RefundableRemaining,
        Refunds = payment.Refunds.Select(RefundResponse.From).ToList()
    };
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public System.DateTimeOffset CreatedAt { get; set; }

    public static RefundResponse From(Refund refund) => new()
    {
        RefundId = refund.PayPalRefundId,
        Amount = refund.Amount,
        Status = refund.Status,
        CreatedAt = refund.CreatedAt
    };
}
