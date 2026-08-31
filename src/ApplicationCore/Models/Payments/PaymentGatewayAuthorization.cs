using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>An authorization (hold) as reported by the payment gateway.</summary>
public class PaymentGatewayAuthorization
{
    public string AuthorizationId { get; set; } = string.Empty;
    public string? GatewayOrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; }
    /// <summary>Safe card display data only, e.g. brand and last four digits.</summary>
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
}
