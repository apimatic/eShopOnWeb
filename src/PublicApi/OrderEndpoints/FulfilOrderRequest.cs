namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record FulfilOrderRequest
{
    public int OrderId { get; init; }
}

public record FulfilOrderResponse
{
    public string CaptureId { get; init; } = "";
    public string CaptureStatus { get; init; } = "";
    public decimal? CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public string Currency { get; init; } = "";
}
