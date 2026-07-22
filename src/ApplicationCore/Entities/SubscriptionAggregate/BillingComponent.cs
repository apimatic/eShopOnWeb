namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A billable add-on that lives on the product family and is therefore available to every
/// subscription within it.
/// </summary>
/// <param name="Kind">The provider's own component kind string, preserved for diagnostics.</param>
/// <param name="IsMetered">Whether usage may be reported against this component.</param>
/// <param name="UnitPrice">Price of a single unit in major currency units (for example dollars).</param>
public sealed record BillingComponent(
    int Id,
    string Handle,
    string Name,
    string Kind,
    bool IsMetered,
    decimal UnitPrice,
    string? PricingScheme,
    string? UnitName);
