using System;
using Microsoft.eShopWeb.PublicApi.PaymentDtos;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse() { }

    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;

    public bool Replayed { get; set; }

    public CaptureDto? Capture { get; set; }

    public decimal RemainingRefundableAmount { get; set; }
}
