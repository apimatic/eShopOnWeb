using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A pay-as-you-go component that lives on a product family and is therefore available to
/// every subscription within it.
/// </summary>
/// <remarks>
/// <see cref="UnitPrice"/> is in whole currency units (dollars). <see cref="IsMetered"/> is the
/// gate UC2 checks before any usage is recorded: a component of the wrong kind cannot accept
/// usage and must be corrected on the provider side (UC0) rather than worked around here.
/// </remarks>
public class MeteredComponent
{
    public MeteredComponent(int id, string handle, string name, string kind, bool isMetered, decimal unitPrice)
    {
        Guard.Against.NullOrEmpty(handle, nameof(handle));
        Guard.Against.NullOrEmpty(name, nameof(name));
        Guard.Against.NullOrEmpty(kind, nameof(kind));
        Guard.Against.Negative(unitPrice, nameof(unitPrice));

        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
        IsMetered = isMetered;
        UnitPrice = unitPrice;
    }

    public int Id { get; }

    public string Handle { get; }

    public string Name { get; }

    /// <summary>The provider's component kind verbatim, so a mismatch can be reported precisely.</summary>
    public string Kind { get; }

    /// <summary>True only when <see cref="Kind"/> is the provider's metered kind.</summary>
    public bool IsMetered { get; }

    /// <summary>Price per unit in whole currency units (dollars).</summary>
    public decimal UnitPrice { get; }

    public bool IsArchived { get; init; }
}
