namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A pay-as-you-go component that usage can be reported against (UC2).
/// </summary>
/// <param name="Id">The provider-assigned numeric identifier.</param>
/// <param name="Handle">The stable, human-authored identifier used in configuration.</param>
/// <param name="Name">Display name.</param>
/// <param name="UnitName">What one unit represents, e.g. "api call".</param>
/// <param name="UnitPriceInCents">Price of a single unit, in cents.</param>
/// <param name="IsMetered">
/// Whether the provider reports this component as metered. Usage may only be recorded against
/// metered components, so this is validated before the first usage call.
/// </param>
/// <param name="IsArchived">Whether the component has been archived.</param>
public record MeteredComponent(
    int Id,
    string Handle,
    string Name,
    string? UnitName,
    long UnitPriceInCents,
    bool IsMetered,
    bool IsArchived)
{
    /// <summary>The unit price expressed in the site's currency unit (dollars).</summary>
    public decimal UnitPrice => UnitPriceInCents / 100m;
}
