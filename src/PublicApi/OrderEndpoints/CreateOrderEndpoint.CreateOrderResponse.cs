using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public string? SquareOrderId { get; set; }

    /// <summary><c>Created</c>, or <c>Pending</c> while Square has not confirmed the order.</summary>
    public string SquareStatus { get; set; } = string.Empty;
    public bool GiftMessageSaved { get; set; }
    public string? Warning { get; set; }
}
