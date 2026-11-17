using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payment;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class OrderPaymentDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long TotalAmountCents { get; set; }
    public string? Currency { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? PaymentCardBrand { get; set; }
    public string? PaymentCardLast4 { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureStatus { get; set; }
    public long? CapturedAmountCents { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? FinixFee { get; set; }
    public decimal? NetProceeds { get; set; }
    public long RefundedAmountCents { get; set; }
    public decimal RefundedAmount { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
    public decimal LineTotal { get; set; }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string FinixRefundId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
    public string? Alias { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReconciliationEntryDto
{
    public string Source { get; set; } = string.Empty;
    public string MatchStatus { get; set; } = string.Empty;
    public string? FinixTransferId { get; set; }
    public string? Type { get; set; }
    public string? State { get; set; }
    public string? Currency { get; set; }
    public long? AmountCents { get; set; }
    public decimal? Amount { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? ParentTransferId { get; set; }
    public int? OrderId { get; set; }
    public int? RefundId { get; set; }
    public string? FinixRefundId { get; set; }
    public string? Note { get; set; }
}

public static class PaymentDtoMapper
{
    public static OrderPaymentDto Map(Order order)
    {
        var totalAmountCents = order.TotalAmountCents ?? order.ResolveTotalAmountCents();

        return new OrderPaymentDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = order.Total(),
            TotalAmountCents = totalAmountCents,
            Currency = order.Currency,
            AuthorizationId = order.AuthorizationId,
            AuthorizationStatus = order.AuthorizationStatus,
            AuthorizationExpiresAt = order.AuthorizationExpiresAt,
            PaymentCardBrand = order.PaymentCardBrand,
            PaymentCardLast4 = order.PaymentCardLast4,
            CaptureTransferId = order.CaptureTransferId,
            CaptureStatus = order.CaptureStatus,
            CapturedAmountCents = order.CapturedAmountCents,
            CapturedAmount = ToCurrency(order.CapturedAmountCents),
            FinixFee = ToCurrency(order.FinixFeeCents),
            NetProceeds = ToCurrency(order.NetAmountCents),
            RefundedAmountCents = order.RefundedAmountCents,
            RefundedAmount = ToCurrency(order.RefundedAmountCents) ?? 0m,
            PaidAt = order.PaidAt,
            FulfilledAt = order.FulfilledAt,
            CancelledAt = order.CancelledAt,
            FailureCode = order.PaymentFailureCode,
            FailureMessage = order.PaymentFailureMessage,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units,
                LineTotal = i.UnitPrice * i.Units
            }).ToList(),
            Refunds = order.Refunds
                .OrderBy(r => r.CreatedAt)
                .Select(Map)
                .ToList()
        };
    }

    public static RefundDto Map(Refund refund)
    {
        return new RefundDto
        {
            RefundId = refund.Id,
            FinixRefundId = refund.FinixRefundId,
            AmountCents = refund.AmountCents,
            Amount = refund.AmountCents / 100m,
            Status = refund.Status,
            IdempotencyKey = refund.IdempotencyKey,
            CreatedAt = refund.CreatedAt
        };
    }

    public static PaymentMethodDto Map(SavedCard card)
    {
        return new PaymentMethodDto
        {
            PaymentMethodId = card.Id,
            Brand = card.Brand,
            Last4 = card.Last4,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            CardholderName = card.CardholderName,
            Alias = card.Alias,
            CreatedAt = card.CreatedAt
        };
    }

    public static ReconciliationEntryDto Map(ReconciliationEntry entry)
    {
        return new ReconciliationEntryDto
        {
            Source = entry.Source,
            MatchStatus = entry.MatchStatus,
            FinixTransferId = entry.FinixTransferId,
            Type = entry.Type,
            State = entry.State,
            Currency = entry.Currency,
            AmountCents = entry.AmountCents,
            Amount = ToCurrency(entry.AmountCents),
            CreatedAt = entry.CreatedAt,
            ParentTransferId = entry.ParentTransferId,
            OrderId = entry.OrderId,
            RefundId = entry.RefundId,
            FinixRefundId = entry.FinixRefundId,
            Note = entry.Note
        };
    }

    private static decimal? ToCurrency(long? cents)
    {
        return cents.HasValue ? cents.Value / 100m : null;
    }

    private static decimal? ToCurrency(decimal? cents)
    {
        return cents.HasValue ? cents.Value / 100m : null;
    }
}
