using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PayOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary>PayPal capture id for the payment, when paid.</summary>
    public string? PayPalCaptureId { get; set; }

    /// <summary>Card network used, when known (e.g. VISA).</summary>
    public string? CardBrand { get; set; }

    /// <summary>Last four digits of the card used, when known.</summary>
    public string? CardLast4 { get; set; }

    public decimal AmountPaid { get; set; }
    public string Currency { get; set; } = "USD";
}
