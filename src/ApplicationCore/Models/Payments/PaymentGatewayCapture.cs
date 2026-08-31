using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>A capture as reported by the payment gateway, including its fee breakdown.</summary>
public class PaymentGatewayCapture
{
    public string CaptureId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? Fee { get; set; }
    public decimal? NetAmount { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
}
