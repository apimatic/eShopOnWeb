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
    public string Status { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CaptureAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string? Currency { get; set; }
}