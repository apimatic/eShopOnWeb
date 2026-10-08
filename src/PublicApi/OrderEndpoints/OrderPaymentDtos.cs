using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public decimal AmountRefunded { get; set; }
    public decimal AmountRefundable { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<PaymentAttemptDto> Payments { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderSummaryDto From(Order order, string defaultCurrency)
    {
        var captured = order.CapturedPayment();
        var currency = captured?.Currency ?? defaultCurrency;
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = currency,
            PaymentStatus = order.PaymentStatus().ToString(),
            AmountPaid = captured?.Amount ?? 0m,
            AmountRefunded = CurrencyMinorUnits.FromMinor(order.RefundedMinor(), currency),
            AmountRefundable = CurrencyMinorUnits.FromMinor(order.RefundableMinor(), currency),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payments = order.Payments.OrderBy(p => p.AttemptNumber).Select(PaymentAttemptDto.From).ToList(),
            Refunds = order.Refunds.OrderBy(r => r.Sequence).Select(RefundDto.From).ToList()
        };
    }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentAttemptDto
{
    public string PaymentId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PspReference { get; set; }
    public string? ResultCode { get; set; }
    public string? RefusalReason { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentAttemptDto From(OrderPayment payment) => new()
    {
        PaymentId = payment.Id,
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.Currency,
        PspReference = payment.PspReference,
        ResultCode = payment.ResultCode,
        RefusalReason = payment.RefusalReason,
        Message = payment.ShopperMessage,
        CreatedAt = payment.CreatedAt
    };
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? PspReference { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto From(OrderRefund refund) => new()
    {
        RefundId = refund.Id,
        Status = refund.Status.ToString(),
        Amount = refund.Amount,
        Currency = refund.Currency,
        Reason = refund.Reason,
        PspReference = refund.PspReference,
        Message = refund.Message,
        CreatedAt = refund.CreatedAt
    };
}
