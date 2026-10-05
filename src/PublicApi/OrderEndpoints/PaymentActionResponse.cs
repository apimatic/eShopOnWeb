using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Result of pay / fulfil / cancel: the order with its payment state afterwards.</summary>
public class PaymentActionResponse : BaseResponse
{
    public PaymentActionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PaymentActionResponse()
    {
    }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    /// <summary>True when the action had already been carried out (a repeated request changes nothing).</summary>
    public bool AlreadyDone { get; set; }
    public OrderDto? Order { get; set; }
}
