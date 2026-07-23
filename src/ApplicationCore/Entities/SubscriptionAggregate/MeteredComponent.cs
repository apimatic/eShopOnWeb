namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The pay-as-you-go component usage is reported against (plan.md UC2).
/// </summary>
public sealed record MeteredComponent
{
    /// <summary>Stable, human-readable identifier (for example <c>api-call</c>).</summary>
    public required string Handle { get; init; }

    /// <summary>Provider-assigned numeric id. Informational only — never persist it.</summary>
    public int? ProviderId { get; init; }

    public required string Name { get; init; }

    /// <summary>The raw component kind the provider reported (for example <c>metered_component</c>).</summary>
    public string? Kind { get; init; }

    /// <summary>
    /// True only when the provider reports the component as metered. Usage is refused otherwise,
    /// because a non-metered component cannot accrue per-unit consumption (plan.md UC2 preconditions).
    /// </summary>
    public bool IsMetered { get; init; }

    /// <summary>The provider's pricing scheme (for example <c>per_unit</c>).</summary>
    public string? PricingScheme { get; init; }

    /// <summary>Price of a single unit in minor units (cents), when the provider reports one.</summary>
    public long? UnitPriceInCents { get; init; }

    /// <summary>Name of one unit (for example <c>call</c>), when the provider reports one.</summary>
    public string? UnitName { get; init; }

    /// <summary>Price of a single unit in major units (dollars).</summary>
    public decimal? UnitPrice => UnitPriceInCents is null ? null : UnitPriceInCents.Value / 100m;
}
