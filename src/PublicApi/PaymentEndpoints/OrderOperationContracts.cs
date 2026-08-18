using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>A bodyless operator action on a single order; the id comes from the route.</summary>
public class OrderOperationRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }
}

/// <summary>The resulting payment state after an operator action (fulfil / cancel).</summary>
public class OrderOperationResponse : BaseResponse
{
    public OrderOperationResponse(Guid correlationId) : base(correlationId) { }
    public OrderOperationResponse() { }

    public int OrderId { get; set; }
    public PaymentDto Payment { get; set; } = new();
}
