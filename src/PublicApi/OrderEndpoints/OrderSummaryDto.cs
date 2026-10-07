using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>An order with its payment state, as its shopper sees it.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountRefunded { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<PaymentSummaryDto> Payments { get; set; } = new();
    public List<RefundSummaryDto> Refunds { get; set; } = new();

    public static OrderSummaryDto From(Order order, string shopCurrency)
    {
        var currency = order.AuthorisedPayment?.Currency ?? order.PaymentAttempts.LastOrDefault()?.Currency ?? shopCurrency;
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            PaymentStatus = order.PaymentStatus.ToString(),
            Currency = currency,
            Total = order.Total(),
            AmountPaid = MinorUnits.FromMinor(order.PaidAmountMinor, currency),
            AmountRefunded = MinorUnits.FromMinor(order.RefundedAmountMinor, currency),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payments = order.PaymentAttempts.OrderBy(a => a.CreatedAt).Select(a => new PaymentSummaryDto
            {
                Reference = a.Reference,
                Status = a.Status.ToString(),
                Amount = MinorUnits.FromMinor(a.AmountMinor, a.Currency),
                Currency = a.Currency,
                PspReference = a.PspReference,
                ResultCode = a.ResultCode,
                RefusalReason = a.RefusalReason,
                CreatedAt = a.CreatedAt
            }).ToList(),
            Refunds = order.Refunds.OrderBy(r => r.CreatedAt).Select(r => new RefundSummaryDto
            {
                RefundId = r.Id,
                Amount = MinorUnits.FromMinor(r.AmountMinor, r.Currency),
                Currency = r.Currency,
                Status = r.Status.ToString(),
                CreatedAt = r.CreatedAt
            }).ToList()
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

public class PaymentSummaryDto
{
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PspReference { get; set; }
    public string? ResultCode { get; set; }
    public string? RefusalReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class RefundSummaryDto
{
    public int RefundId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
