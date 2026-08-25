namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Bundles the caller's identity with the per-request scoped service an endpoint needs, so a
/// shopper-scoped <c>MinimalApi.Endpoint</c> handler can still satisfy the library's two-parameter
/// <c>HandleAsync(TRequest, TDep)</c> contract while also knowing who is calling.
/// </summary>
public record BuyerContext<TService>(string BuyerId, TService Service);
