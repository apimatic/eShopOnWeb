using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Result of an operator lifecycle action on an order.</summary>
public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId) { }
    public OrderActionResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
