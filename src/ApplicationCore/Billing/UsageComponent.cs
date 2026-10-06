namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A metered / usage component published on the subscription product family
/// (Maxio component, e.g. the seeded "api-call" metered component).
/// </summary>
public record UsageComponent
{
    public long Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>Maxio component kind, e.g. "metered_component".</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Name of the billable unit, e.g. "api call".</summary>
    public string? UnitName { get; init; }

    /// <summary>Price per unit as reported by Maxio (already a decimal unit price, not cents).</summary>
    public decimal? UnitPrice { get; init; }

    public bool Taxable { get; init; }

    public bool Archived { get; init; }
}
