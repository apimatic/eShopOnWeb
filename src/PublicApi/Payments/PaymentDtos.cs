using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// What a caller is told about a payment: the state of the hold, of the capture and of the refunds,
/// carried with the processor's own identifiers so a later request can act on them.
/// </summary>
public class PaymentDto
{
    public int PaymentId { get; init; }
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public decimal AuthorizedAmount { get; init; }
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public DateTimeOffset? AuthorizationExpires { get; init; }
    public bool HoldRenewed { get; init; }
    public string? RenewedFromAuthorizationId { get; init; }
    public CaptureDto? Capture { get; init; }
    public decimal RefundedAmount { get; init; }
    public decimal RefundableAmount { get; init; }
    public IReadOnlyList<RefundDto> Refunds { get; init; } = new List<RefundDto>();
    public string PaidWith { get; init; } = string.Empty;

    public static PaymentDto From(OrderPayment payment) => new()
    {
        PaymentId = payment.Id,
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        Currency = payment.Currency,
        AuthorizedAmount = payment.Amount,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpires = payment.AuthorizationExpiration,
        HoldRenewed = payment.RenewalCount > 0,
        RenewedFromAuthorizationId = payment.RenewedFromAuthorizationId,
        Capture = payment.CaptureId is null
            ? null
            : new CaptureDto
            {
                CaptureId = payment.CaptureId,
                Status = payment.CaptureStatus ?? string.Empty,
                GrossAmount = payment.CapturedAmount ?? 0m,
                FeeAmount = payment.FeeAmount ?? 0m,
                NetAmount = payment.NetAmount ?? 0m,
                Currency = payment.Currency,
                CapturedAt = payment.CapturedDate
            },
        RefundedAmount = payment.RefundedAmount,
        RefundableAmount = payment.RefundableAmount,
        Refunds = payment.Refunds.Select(RefundDto.From).ToList(),
        PaidWith = payment.PaymentMethodId is null ? "a one-off card" : $"saved card {payment.PaymentMethodId}"
    };
}

/// <summary>What the processor reported when the money was taken.</summary>
public class CaptureDto
{
    public string CaptureId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal GrossAmount { get; init; }
    public decimal FeeAmount { get; init; }
    public decimal NetAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateTimeOffset? CapturedAt { get; init; }
}

public class RefundDto
{
    public int RefundId { get; init; }
    public string? PayPalRefundId { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public decimal? FeeReturned { get; init; }
    public decimal? NetAmount { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public DateTimeOffset Requested { get; init; }
    public DateTimeOffset? Completed { get; init; }

    public static RefundDto From(PaymentRefund refund) => new()
    {
        RefundId = refund.Id,
        PayPalRefundId = refund.PayPalRefundId,
        Status = refund.Status.ToString(),
        Amount = refund.Amount,
        Currency = refund.Currency,
        FeeReturned = refund.FeeReturned,
        NetAmount = refund.NetAmount,
        IdempotencyKey = refund.IdempotencyKey,
        Requested = refund.Requested,
        Completed = refund.Completed
    };
}

public class OrderLineDto
{
    public int CatalogItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Units { get; init; }
    public decimal LineTotal { get; init; }
}

/// <summary>An order together with the state of its money.</summary>
public class PlacedOrderDto
{
    public int OrderId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public IReadOnlyList<OrderLineDto> Items { get; init; } = new List<OrderLineDto>();
    public PaymentDto? Payment { get; init; }

    public static PlacedOrderDto From(Order order, OrderPayment? payment) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Status = order.Status.ToString(),
        Total = order.Total(),
        Items = order.OrderItems.Select(item => new OrderLineDto
        {
            CatalogItemId = item.ItemOrdered.CatalogItemId,
            ProductName = item.ItemOrdered.ProductName,
            UnitPrice = item.UnitPrice,
            Units = item.Units,
            LineTotal = item.UnitPrice * item.Units
        }).ToList(),
        Payment = payment is null ? null : PaymentDto.From(payment)
    };
}

/// <summary>
/// A saved card, described only in terms that let the shopper recognise it. There is no card number
/// here because there is none kept.
/// </summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; init; }
    public string Brand { get; init; } = string.Empty;
    public string Last4 { get; init; } = string.Empty;
    public string? Expiry { get; init; }
    public string? CardHolderName { get; init; }
    public string? Nickname { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset Created { get; init; }

    public static PaymentMethodDto From(PaymentMethod card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand ?? "UNKNOWN",
        Last4 = card.Last4 ?? "----",
        Expiry = card.Expiry,
        CardHolderName = card.CardHolderName,
        Nickname = card.Alias,
        Description = card.Description,
        Created = card.Created
    };
}
