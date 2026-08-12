namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Result of an operator moving an order (dispatch/cancel).</summary>
public class OrderTransitionResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
