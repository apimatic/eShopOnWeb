namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Result of an operator order-state change (dispatch/cancel).</summary>
public class OrderStateResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
