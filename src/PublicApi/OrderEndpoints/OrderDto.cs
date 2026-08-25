using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentDto
{
    public string Provider { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public static class OrderMapper
{
    public static OrderDto ToDto(Order order) => new()
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
        Payment = order.Payment is null ? null : new PaymentDto
        {
            Provider = order.Payment.Provider,
            Currency = order.Payment.Currency,
            PayPalOrderId = order.Payment.PayPalOrderId,
            AuthorizationId = order.Payment.AuthorizationId,
            AuthorizationStatus = order.Payment.AuthorizationStatus,
            CaptureId = order.Payment.CaptureId,
            CaptureStatus = order.Payment.CaptureStatus,
            CapturedAmount = order.Payment.CapturedAmount,
            PayPalFee = order.Payment.PayPalFee,
            NetAmount = order.Payment.NetAmount,
            RefundedAmount = order.Payment.RefundedAmount,
            Refunds = order.Payment.Refunds.Select(r => new RefundDto
            {
                PayPalRefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status
            }).ToList()
        }
    };
}
