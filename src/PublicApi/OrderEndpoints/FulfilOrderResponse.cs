using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public string? CaptureId { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
}
