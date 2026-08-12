namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Result of an operator action (dispatch/cancel) on an order.</summary>
public class OrderActionResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
