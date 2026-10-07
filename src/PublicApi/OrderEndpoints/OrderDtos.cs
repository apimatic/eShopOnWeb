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

public class PaymentAttemptDto
{
    public int AttemptNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string MerchantReference { get; set; } = string.Empty;
    public string? PspReference { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? ResultCode { get; set; }
    public string? RefusalReason { get; set; }
    public string? RefusalReasonCode { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class RefundDto
{
    public Guid RefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string MerchantReference { get; set; } = string.Empty;
    public string? PspReference { get; set; }
    public string? Reason { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public decimal AmountRefunded { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<PaymentAttemptDto> Payments { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();
}

internal static class OrderDtoMapper
{
    public static OrderItemDto ToDto(OrderItem item) => new()
    {
        CatalogItemId = item.ItemOrdered.CatalogItemId,
        ProductName = item.ItemOrdered.ProductName,
        UnitPrice = item.UnitPrice,
        Units = item.Units,
    };

    public static PaymentAttemptDto ToDto(OrderPaymentAttempt attempt) => new()
    {
        AttemptNumber = attempt.AttemptNumber,
        Status = attempt.Status.ToString(),
        MerchantReference = attempt.MerchantReference,
        PspReference = attempt.PspReference,
        Amount = Money.FromMinorUnits(attempt.AmountMinor, attempt.Currency),
        Currency = attempt.Currency,
        ResultCode = attempt.ResultCode,
        RefusalReason = attempt.RefusalReason,
        RefusalReasonCode = attempt.RefusalReasonCode,
        Message = attempt.ShopperMessage,
        CreatedAt = attempt.CreatedAt,
        CompletedAt = attempt.CompletedAt,
    };

    public static RefundDto ToDto(OrderRefund refund) => new()
    {
        RefundId = refund.Id,
        Status = refund.Status.ToString(),
        Amount = Money.FromMinorUnits(refund.AmountMinor, refund.Currency),
        Currency = refund.Currency,
        MerchantReference = refund.MerchantReference,
        PspReference = refund.PspReference,
        Reason = refund.Reason,
        FailureMessage = refund.FailureMessage,
        CreatedAt = refund.CreatedAt,
        CompletedAt = refund.CompletedAt,
    };

    /// <param name="defaultCurrency">The configured currency, shown for orders that have no payment yet.</param>
    public static OrderSummaryDto ToSummary(Order order, string defaultCurrency)
    {
        var paid = order.AuthorisedPayment;
        var currency = paid?.Currency
            ?? order.PaymentAttempts.OrderByDescending(a => a.AttemptNumber).FirstOrDefault()?.Currency
            ?? defaultCurrency;
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = currency,
            AmountPaid = paid is null ? 0m : Money.FromMinorUnits(paid.AmountMinor, paid.Currency),
            AmountRefunded = paid is null ? 0m : Money.FromMinorUnits(order.RefundedMinor, paid.Currency),
            Items = order.OrderItems.Select(ToDto).ToList(),
            Payments = order.PaymentAttempts.OrderBy(a => a.AttemptNumber).Select(ToDto).ToList(),
            Refunds = order.Refunds.OrderBy(r => r.CreatedAt).Select(ToDto).ToList(),
        };
    }
}
