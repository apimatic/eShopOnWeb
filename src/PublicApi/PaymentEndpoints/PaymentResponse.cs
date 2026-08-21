using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentService;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>The payment state returned by pay / fulfil / cancel.</summary>
public class PaymentResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public static PaymentResponse From(PaymentResult r) => new()
    {
        OrderId = r.OrderId,
        PaymentStatus = r.PaymentStatus,
        CurrencyCode = r.CurrencyCode,
        PayPalOrderId = r.PayPalOrderId,
        AuthorizationId = r.AuthorizationId,
        AuthorizationStatus = r.AuthorizationStatus,
        AuthorizationExpiresAt = r.AuthorizationExpiresAt,
        CaptureId = r.CaptureId,
        CaptureStatus = r.CaptureStatus,
        CapturedAmount = r.CapturedAmount,
        PayPalFee = r.PayPalFee,
        NetAmount = r.NetAmount
    };
}
