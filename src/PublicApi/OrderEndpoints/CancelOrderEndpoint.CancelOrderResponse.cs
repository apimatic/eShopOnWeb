using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse() : base(Guid.NewGuid())
    {
    }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
}