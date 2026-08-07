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

    /// <summary>The order's payment state after this call (e.g. "Paid").</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? PayPalCaptureId { get; set; }

    /// <summary>Brand of the card charged (only present on a fresh charge), e.g. VISA.</summary>
    public string? CardBrand { get; set; }

    /// <summary>Last four digits of the card charged (only present on a fresh charge).</summary>
    public string? Last4 { get; set; }

    /// <summary>True when the order was already paid and no new charge was made (idempotent replay).</summary>
    public bool AlreadyPaid { get; set; }
}
