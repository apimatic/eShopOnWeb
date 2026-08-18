namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>The order's identity and its (possibly just-changed) fulfilment status.</summary>
public class OrderStatusResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
