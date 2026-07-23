using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A pay-as-you-go component published on a product family. Only components reporting
/// <see cref="IsMetered"/> may receive usage — see <c>ISubscriptionService.RecordUsageAsync</c>.
/// </summary>
public class MeteredComponent
{
    public MeteredComponent(int id, string handle, string name, string kind)
    {
        Guard.Against.NullOrEmpty(handle, nameof(handle));
        Guard.Against.NullOrEmpty(name, nameof(name));
        Guard.Against.NullOrEmpty(kind, nameof(kind));

        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
    }

    public int Id { get; }

    public string Handle { get; }

    public string Name { get; }

    /// <summary>The provider's component kind discriminator, preserved verbatim.</summary>
    public string Kind { get; }

    /// <summary>True only when the provider reports this component as metered.</summary>
    public bool IsMetered { get; init; }

    /// <summary>Price per unit in whole currency units (dollars).</summary>
    public decimal? UnitPrice { get; init; }

    public string? UnitName { get; init; }

    public string? PricingScheme { get; init; }
}
