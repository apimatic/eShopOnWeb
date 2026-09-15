namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>A request identified only by an order id taken from the route.</summary>
public record OrderIdRequest(int OrderId);
