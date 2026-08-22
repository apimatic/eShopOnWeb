namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
