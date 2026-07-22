using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A provider-agnostic view of the metered component the pay-as-you-go flow (UC2) bills against.
/// Used by the startup / first-call validation that the configured handle resolves to a component
/// of metered kind on the family.
/// </summary>
public class MeteredComponentInfo
{
    public MeteredComponentInfo(int id, string handle, string kind, decimal? unitPrice)
    {
        Id = id;
        Handle = handle;
        Kind = kind;
        UnitPrice = unitPrice;
    }

    public int Id { get; }

    public string Handle { get; }

    /// <summary>The provider component kind, e.g. <c>metered_component</c>.</summary>
    public string Kind { get; }

    /// <summary>The per-unit price in whole currency units (dollars), when the scheme is per-unit.</summary>
    public decimal? UnitPrice { get; }

    /// <summary>True when the component is of metered kind (the only kind UC2 accepts).</summary>
    public bool IsMetered => string.Equals(Kind, "metered_component", StringComparison.OrdinalIgnoreCase);
}
