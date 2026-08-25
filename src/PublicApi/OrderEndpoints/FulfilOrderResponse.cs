namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse
{
    public string CaptureId { get; set; } = "";
    public string CapturedAmount { get; set; } = "";
    public string Currency { get; set; } = "";
    public string? PayPalFee { get; set; }
    public string? NetAmount { get; set; }
    public string Status { get; set; } = "";
}
