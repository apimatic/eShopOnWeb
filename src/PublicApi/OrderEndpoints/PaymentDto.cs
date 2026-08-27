using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The payment state of an order, including the PayPal-owned ids and statuses needed to
/// act on the hold, the capture and the refunds in later requests.
/// </summary>
public class PaymentDto
{
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal OrderTotal { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public decimal? AuthorizedAmount { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentDto FromEntity(Payment payment) => new()
    {
        PaymentId = payment.Id,
        Status = payment.Status.ToString(),
        OrderTotal = payment.OrderTotal,
        Currency = payment.Currency,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizedAmount = payment.AuthorizedAmount,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded,
        RefundableAmount = payment.RefundableAmount,
        Refunds = payment.Refunds.Select(RefundDto.FromEntity).ToList()
    };
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto FromEntity(PaymentRefund refund) => new()
    {
        RefundId = refund.PayPalRefundId,
        Amount = refund.Amount,
        Status = refund.Status,
        IdempotencyKey = refund.IdempotencyKey,
        CreatedAt = refund.CreatedAt
    };
}
