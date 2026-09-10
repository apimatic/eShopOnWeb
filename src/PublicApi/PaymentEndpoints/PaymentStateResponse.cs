using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Safe, PayPal-backed payment state returned by the pay/fulfil/cancel endpoints.</summary>
public class PaymentStateResponse : BaseResponse
{
    public PaymentStateResponse(Guid correlationId) : base(correlationId) { }
    public PaymentStateResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public decimal? CapturedGross { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal RefundedToDate { get; set; }
    public decimal RefundableRemaining { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public List<RefundView> Refunds { get; set; } = new();

    public class RefundView
    {
        public int RefundId { get; set; }
        public decimal Amount { get; set; }
        public string? PayPalRefundId { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset CreatedDate { get; set; }
    }

    public static PaymentStateResponse From(OrderPayment payment, Guid correlationId) => new(correlationId)
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.CurrencyCode,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CapturedGross = payment.CapturedGross,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        RefundedToDate = payment.RefundedToDate(),
        RefundableRemaining = payment.RefundableRemaining(),
        SavedPaymentMethodId = payment.SavedPaymentMethodId,
        Refunds = payment.Refunds
            .OrderBy(r => r.CreatedDate)
            .Select(r => new RefundView
            {
                RefundId = r.Id,
                Amount = r.Amount,
                PayPalRefundId = r.PayPalRefundId,
                Status = r.Status,
                CreatedDate = r.CreatedDate
            })
            .ToList()
    };
}
