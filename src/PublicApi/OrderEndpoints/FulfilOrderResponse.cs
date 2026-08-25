namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string CaptureId { get; set; } = string.Empty;
    public string CaptureStatus { get; set; } = string.Empty;
    public decimal CapturedAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
}
