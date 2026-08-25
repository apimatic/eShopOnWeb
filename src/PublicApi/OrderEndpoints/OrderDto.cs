using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = default!;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }

    public static OrderDto FromOrder(Order order) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Payment = order.Payment is null ? null : PaymentDto.FromPayment(order.Payment)
    };
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = default!;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentDto
{
    public string AuthorizationId { get; set; } = default!;
    public string AuthorizationStatus { get; set; } = default!;
    public decimal AuthorizedAmount { get; set; }
    public string Currency { get; set; } = default!;
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFeeAmount { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentDto FromPayment(Payment payment) => new()
    {
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizedAmount = payment.AuthorizedAmount,
        Currency = payment.Currency,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFeeAmount = payment.PayPalFeeAmount,
        NetAmount = payment.NetAmount,
        RefundedAmount = payment.TotalRefunded,
        Refunds = payment.Refunds.Select(r => new RefundDto
        {
            RefundId = r.Id,
            PayPalRefundId = r.PayPalRefundId,
            Amount = r.Amount,
            Status = r.Status
        }).ToList()
    };
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Status { get; set; } = default!;
}
