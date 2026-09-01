using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpointsShared;

/// <summary>Safe, display-ready view of an order's payment state.</summary>
public class PaymentDto
{
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLastDigits { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public string? LastFailureReason { get; set; }

    public static PaymentDto FromModel(OrderPayment payment)
        => new PaymentDto
        {
            PaymentId = payment.Id,
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.Currency,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.PayPalAuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.PayPalCaptureId,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            CapturedAt = payment.CapturedAt,
            CardBrand = payment.CardBrand,
            CardLastDigits = payment.CardLastDigits,
            SavedPaymentMethodId = payment.SavedPaymentMethodId,
            TotalRefunded = payment.TotalRefunded(),
            RemainingRefundable = payment.RemainingRefundable(),
            LastFailureReason = payment.LastFailureReason
        };
}
