using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Describes an order's payment state after a pay or refund operation.</summary>
public class PaymentStatusResponse : BaseResponse
{
    public PaymentStatusResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PaymentStatusResponse()
    {
    }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? PayPalOrderId { get; set; }
    public string? CaptureId { get; set; }
    public string? RefundId { get; set; }

    public static PaymentStatusResponse From(PaymentResult result, Guid correlationId) => new(correlationId)
    {
        OrderId = result.OrderId,
        PaymentStatus = result.PaymentStatus.ToString(),
        Amount = result.Amount,
        Currency = result.CurrencyCode,
        PayPalOrderId = result.PayPalOrderId,
        CaptureId = result.CaptureId,
        RefundId = result.RefundId
    };
}
