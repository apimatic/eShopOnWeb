using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PaymentDto
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public bool AuthorizationVoided { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public long AuthorizedAmountCents { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal CapturedAmount { get; set; }
    public long CapturedAmountCents { get; set; }
    public decimal Fee { get; set; }
    public decimal FeeCents { get; set; }
    public decimal Net { get; set; }
    public decimal NetCents { get; set; }
    public decimal RefundedAmount { get; set; }
    public long RefundedAmountCents { get; set; }
    public decimal RemainingRefundable { get; set; }
    public long RemainingRefundableCents { get; set; }
    public string? CardBrand { get; set; }
    public string? Last4 { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string RefundTransferId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public long AmountCents { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
    public decimal LineTotal { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public static class PaymentMapping
{
    public static PaymentDto ToDto(Order order, Payment? payment, IEnumerable<PaymentRefund> refunds, string currency)
    {
        var dto = new PaymentDto
        {
            OrderId = order.Id,
            OrderStatus = order.Status.ToString(),
            Currency = payment?.Currency ?? currency
        };

        if (payment is not null)
        {
            dto.AuthorizationId = payment.FinixAuthorizationId;
            dto.AuthorizationStatus = payment.AuthorizationState;
            dto.AuthorizationVoided = payment.AuthorizationVoided;
            dto.AuthorizedAmountCents = payment.AuthorizedAmountCents;
            dto.AuthorizedAmount = ToAmount(payment.AuthorizedAmountCents);
            dto.CaptureId = payment.FinixTransferId;
            dto.CaptureStatus = payment.TransferState;
            dto.CapturedAmountCents = payment.CapturedAmountCents;
            dto.CapturedAmount = ToAmount(payment.CapturedAmountCents);
            dto.FeeCents = payment.FeeAmountCents;
            dto.Fee = ToAmount(payment.FeeAmountCents);
            dto.NetCents = payment.NetAmountCents;
            dto.Net = ToAmount(payment.NetAmountCents);
            dto.RefundedAmountCents = payment.RefundedAmountCents;
            dto.RefundedAmount = ToAmount(payment.RefundedAmountCents);
            dto.RemainingRefundableCents = payment.RefundableAmountCents;
            dto.RemainingRefundable = ToAmount(payment.RefundableAmountCents);
            dto.CardBrand = payment.CardBrand;
            dto.Last4 = payment.Last4;
            dto.FailureCode = payment.FailureCode;
            dto.FailureMessage = payment.FailureMessage;
            dto.CreatedAt = payment.CreatedAt;
            dto.UpdatedAt = payment.UpdatedAt;
        }

        dto.Refunds = refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundDto
            {
                RefundId = r.Id,
                RefundTransferId = r.FinixRefundTransferId,
                Amount = ToAmount(r.AmountCents),
                AmountCents = r.AmountCents,
                Status = r.State,
                IdempotencyKey = r.IdempotencyKey,
                CreatedAt = r.CreatedAt
            })
            .ToList();

        return dto;
    }

    public static OrderDto ToOrderDto(Order order, Payment? payment, IEnumerable<PaymentRefund> refunds, string currency)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = payment?.Currency ?? currency,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units,
                LineTotal = i.UnitPrice * i.Units
            }).ToList(),
            Payment = payment is null ? null : ToDto(order, payment, refunds, currency)
        };
    }

    public static decimal ToAmount(long cents) => cents / 100m;

    public static decimal ToAmount(decimal cents) => Math.Round(cents / 100m, 2, MidpointRounding.AwayFromZero);

    public static long ToCents(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
