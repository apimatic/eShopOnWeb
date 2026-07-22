using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A billable add-on component that lives on a product family, normalized from the provider.
/// UC2 requires the configured component to be of metered kind.
/// </summary>
public class BillingComponent
{
    /// <summary>The provider's identifier for a usage-metered component kind.</summary>
    public const string MeteredKind = "metered_component";

    public int Id { get; init; }
    public string? Handle { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>The component kind as reported by the provider, e.g. "metered_component".</summary>
    public string Kind { get; init; } = string.Empty;
    public string? PricingScheme { get; init; }

    /// <summary>The per-unit price in major currency units (e.g. 0.01 — not cents).</summary>
    public decimal? UnitPrice { get; init; }
    public int ProductFamilyId { get; init; }
    public string? ProductFamilyHandle { get; init; }
    public bool Archived { get; init; }

    public bool IsMetered => string.Equals(Kind, MeteredKind, StringComparison.OrdinalIgnoreCase);
}
