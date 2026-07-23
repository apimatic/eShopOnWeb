namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A billable component attached to a product family, normalized from the billing provider.
/// Usage may only be reported against a component whose <see cref="IsMetered"/> is true.
/// </summary>
public class MeteredComponentInfo
{
    public int Id { get; init; }

    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? UnitName { get; init; }

    /// <summary>The raw provider component kind (for example "metered_component").</summary>
    public string? Kind { get; init; }

    /// <summary>True only when the provider reports this component as metered.</summary>
    public bool IsMetered { get; init; }

    public string? PricingScheme { get; init; }

    /// <summary>Price per consumed unit in decimal currency units, when the provider exposes one.</summary>
    public decimal? UnitPrice { get; init; }

    public bool IsArchived { get; init; }
}
