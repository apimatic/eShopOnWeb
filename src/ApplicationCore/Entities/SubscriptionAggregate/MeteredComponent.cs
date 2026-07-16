namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The resolved, kind-validated metered component (UC2's pay-as-you-go unit, e.g. "api-call").
/// </summary>
public sealed record MeteredComponent(int Id, string Handle, string? UnitPrice);
