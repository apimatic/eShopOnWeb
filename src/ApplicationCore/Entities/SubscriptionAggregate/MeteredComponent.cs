namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A pay-as-you-go component that usage can be reported against (UC2).
/// </summary>
/// <remarks>
/// The component lives on the product family, so it is available to every subscription on any
/// plan in that family without a per-subscription attach step.
/// </remarks>
public sealed record MeteredComponent
{
    public required int Id { get; init; }

    public required string Handle { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// True only when the provider reports this component as metered. Usage may not be recorded
    /// against components of any other kind, so this is validated before the first usage call.
    /// </summary>
    public required bool IsMetered { get; init; }

    /// <summary>The raw component kind the provider reported, retained for diagnostics.</summary>
    public string? Kind { get; init; }

    /// <summary>The per-unit price in whole currency units (for example 0.01).</summary>
    public decimal? UnitPrice { get; init; }

    /// <summary>What one unit is called, for example "call".</summary>
    public string? UnitName { get; init; }

    public string? ProductFamilyHandle { get; init; }

    public bool IsArchived { get; init; }
}
