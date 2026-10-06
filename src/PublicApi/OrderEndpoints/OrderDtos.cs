using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderPaymentDto
{
    public int AttemptNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PspReference { get; set; }
    public string? ResultCode { get; set; }
    public string? RefusalReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class OrderRefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PspReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public decimal AmountRefunded { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderPaymentDto> Payments { get; set; } = new();
    public List<OrderRefundDto> Refunds { get; set; } = new();

    public static OrderSummaryDto From(Order order, string configuredCurrency)
    {
        var payment = order.AuthorisedPayment;
        var currency = payment?.Currency ?? configuredCurrency;
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = currency,
            PaymentStatus = order.PaymentStatus.ToString(),
            AmountPaid = payment is null ? 0m : MinorUnits.ToDecimal(payment.AmountMinorUnits, payment.Currency),
            AmountRefunded = MinorUnits.ToDecimal(order.RefundedMinorUnits, currency),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payments = order.PaymentAttempts.OrderBy(a => a.AttemptNumber).Select(a => new OrderPaymentDto
            {
                AttemptNumber = a.AttemptNumber,
                Status = a.Status.ToString(),
                Amount = MinorUnits.ToDecimal(a.AmountMinorUnits, a.Currency),
                Currency = a.Currency,
                PspReference = a.PspReference,
                ResultCode = a.ResultCode,
                RefusalReason = a.RefusalReason,
                CreatedAt = a.CreatedAt,
                CompletedAt = a.CompletedAt
            }).ToList(),
            Refunds = order.Refunds.OrderBy(r => r.Sequence).Select(r => new OrderRefundDto
            {
                RefundId = r.RefundId,
                Status = r.Status.ToString(),
                Amount = MinorUnits.ToDecimal(r.AmountMinorUnits, r.Currency),
                Currency = r.Currency,
                PspReference = r.PspReference,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }
}
