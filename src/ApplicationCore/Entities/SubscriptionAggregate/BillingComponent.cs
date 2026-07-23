using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A billable add-on that lives on a product family and is therefore available to every
/// subscription within it (UC2's metered <c>api-call</c> component).
/// </summary>
public sealed class BillingComponent
{
    public BillingComponent(long id,
        string handle,
        string name,
        string kind,
        string? unitName,
        decimal? unitPrice,
        string pricingScheme)
    {
        Id = Guard.Against.NegativeOrZero(id, nameof(id));
        Handle = Guard.Against.NullOrWhiteSpace(handle, nameof(handle));
        Name = Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Kind = Guard.Against.NullOrWhiteSpace(kind, nameof(kind));
        UnitName = unitName;
        UnitPrice = unitPrice;
        PricingScheme = pricingScheme;
    }

    public long Id { get; }

    public string Handle { get; }

    public string Name { get; }

    /// <summary>The provider's component kind verbatim; see <see cref="IsMetered"/> for the check UC2 relies on.</summary>
    public string Kind { get; }

    public string? UnitName { get; }

    /// <summary>Price per unit in major units (e.g. 0.01 dollars). Null when the scheme is not per-unit.</summary>
    public decimal? UnitPrice { get; }

    public string PricingScheme { get; }

    /// <summary>
    /// True only for metered components. UC2 refuses to record usage unless this holds,
    /// because a non-metered component cannot accept usage records.
    /// </summary>
    public bool IsMetered => Kind.Equals("metered_component", StringComparison.OrdinalIgnoreCase);
}
