namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Result of an operator order transition (dispatch/cancel).</summary>
public class OrderActionResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
