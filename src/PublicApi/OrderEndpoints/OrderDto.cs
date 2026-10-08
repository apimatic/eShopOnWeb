using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto Payment { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderDto From(Order order, IEnumerable<PaymentAttempt>? attempts = null, IEnumerable<OrderRefund>? refunds = null)
    {
        var lastAttempt = attempts?.OrderBy(a => a.AttemptNumber).LastOrDefault();
        return new OrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.Currency,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = PaymentDto.From(order, lastAttempt),
            Refunds = refunds?.Select(RefundDto.From).ToList() ?? new()
        };
    }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class PaymentDto
{
    /// <summary>AwaitingPayment, PaymentPending, Paid, PartiallyRefunded or Refunded.</summary>
    public string Status { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public decimal AmountRefunded { get; set; }
    public decimal RefundableAmount { get; set; }
    public string? PspReference { get; set; }
    public DateTimeOffset? PaidDate { get; set; }

    /// <summary>Why the most recent attempt did not take payment, if it did not.</summary>
    public string? LastRefusalReason { get; set; }

    public static PaymentDto From(Order order, PaymentAttempt? lastAttempt) => new()
    {
        Status = order.PaymentStatus.ToString(),
        AmountPaid = order.AmountPaid,
        AmountRefunded = order.AmountRefunded,
        RefundableAmount = order.RefundableAmount,
        PspReference = order.PaymentPspReference,
        PaidDate = order.PaidDate,
        LastRefusalReason = order.IsPaid ? null : lastAttempt?.RefusalReason
    };
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>InFlight, Received, Failed or Unknown.</summary>
    public string Status { get; set; } = string.Empty;
    public string? PspReference { get; set; }
    public string? Reason { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static RefundDto From(OrderRefund refund) => new()
    {
        RefundId = refund.Id,
        Amount = refund.Amount,
        Currency = refund.Currency,
        Status = refund.Status.ToString(),
        PspReference = refund.PspReference,
        Reason = refund.Reason,
        FailureReason = refund.FailureReason,
        CreatedDate = refund.CreatedDate
    };
}
