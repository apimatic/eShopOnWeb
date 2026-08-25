using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PayOrderResponse()
    {
    }

    public int OrderId { get; set; }

    /// <summary>"Authorized" or "RequiresAction".</summary>
    public string Status { get; set; } = default!;

    /// <summary>Set only when Status is "RequiresAction" — PayPal requires the shopper to complete a browser challenge at this URL before the payment can be authorized.</summary>
    public string? PayerActionUrl { get; set; }

    public string? AuthorizationId { get; set; }
    public decimal? AuthorizedAmount { get; set; }
    public string? Currency { get; set; }
}
