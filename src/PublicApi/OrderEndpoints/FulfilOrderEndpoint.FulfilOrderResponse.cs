namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse
{
    public int OrderId { get; set; }
    public string? Status { get; set; }
    public string? CaptureId { get; set; }
    public decimal CapturedAmount { get; set; }
    public string? Currency { get; set; }
}
