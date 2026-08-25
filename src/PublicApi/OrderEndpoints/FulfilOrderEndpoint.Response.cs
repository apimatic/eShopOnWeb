namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse
{
    public int OrderId { get; set; }
    public string CaptureId { get; set; } = "";
    public decimal CapturedAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
    public string CaptureStatus { get; set; } = "";
}
