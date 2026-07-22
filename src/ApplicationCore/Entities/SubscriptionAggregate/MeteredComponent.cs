using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The pay-as-you-go component usage is reported against (UC2). <see cref="UnitPrice"/> is in dollars
/// per unit.
/// </summary>
public sealed record MeteredComponent
{
    public MeteredComponent(int id, string handle, string name, string kind, bool isMetered, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(handle)) throw new ArgumentException("A component handle is required.", nameof(handle));

        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
        IsMetered = isMetered;
        UnitPrice = unitPrice;
    }

    public int Id { get; init; }

    public string Handle { get; init; }

    public string Name { get; init; }

    /// <summary>The provider's component kind string, e.g. <c>metered_component</c>.</summary>
    public string Kind { get; init; }

    /// <summary>True only when <see cref="Kind"/> is the provider's metered kind. UC2 refuses to record usage otherwise.</summary>
    public bool IsMetered { get; init; }

    /// <summary>Price per unit, in dollars (e.g. 0.01m for $0.01 per API call).</summary>
    public decimal UnitPrice { get; init; }

    public string? UnitName { get; init; }

    public string? PricingScheme { get; init; }

    public bool IsArchived { get; init; }
}
