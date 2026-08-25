namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record CancelOrderRequest
{
    public int OrderId { get; init; }
}

public record CancelOrderResponse
{
    public int OrderId { get; init; }
    public string Status { get; init; } = "";
}
