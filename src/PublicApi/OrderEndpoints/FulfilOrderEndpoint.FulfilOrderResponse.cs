using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public FulfilOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public string PayPalCaptureId { get; set; } = string.Empty;
    public string CaptureStatus { get; set; } = string.Empty;
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFeeAmount { get; set; }
    public decimal? NetAmount { get; set; }
}
