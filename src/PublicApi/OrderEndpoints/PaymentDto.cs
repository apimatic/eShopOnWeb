using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// API view of a payment: the PayPal-owned ids and statuses for the hold,
/// the capture (with fee breakdown) and the refunds.
/// </summary>
public class PaymentDto
{
    public string Currency { get; set; } = string.Empty;
    public decimal OrderTotal { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? InvoiceId { get; set; }
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
    public List<RefundDto> Refunds { get; set; } = new List<RefundDto>();

    public static PaymentDto FromPayment(Payment payment)
    {
        return new PaymentDto
        {
            Currency = payment.Currency,
            OrderTotal = payment.OrderTotal,
            PayPalOrderId = payment.PayPalOrderId,
            InvoiceId = payment.InvoiceId,
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
            Refunds = payment.Refunds.Select(RefundDto.FromRefund).ToList()
        };
    }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto FromRefund(PaymentRefund refund)
    {
        return new RefundDto
        {
            RefundId = refund.PayPalRefundId,
            IdempotencyKey = refund.IdempotencyKey,
            Amount = refund.Amount,
            Status = refund.Status,
            Note = refund.Note,
            CreatedAt = refund.CreatedAt
        };
    }
}
