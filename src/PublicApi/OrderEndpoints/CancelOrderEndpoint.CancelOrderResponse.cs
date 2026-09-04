using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse() { }

    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>True when a hold existed and was released (voided) so no money ever moved.</summary>
    public bool FundsReleased { get; set; }

    public bool Replayed { get; set; }
}
