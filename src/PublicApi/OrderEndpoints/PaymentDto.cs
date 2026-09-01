using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentDto FromModel(Payment payment) => new()
    {
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.Currency,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        CapturedAt = payment.CapturedAt,
        TotalRefunded = payment.TotalRefunded,
        RefundableRemaining = payment.RefundableRemaining,
        Refunds = payment.Refunds.Select(RefundDto.FromModel).ToList()
    };
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto FromModel(PaymentRefund refund) => new()
    {
        RefundId = refund.Id,
        PayPalRefundId = refund.PayPalRefundId,
        IdempotencyKey = refund.IdempotencyKey,
        Amount = refund.Amount,
        Status = refund.Status,
        CreatedAt = refund.CreatedAt
    };
}
