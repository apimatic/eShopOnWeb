namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Describes the eShopOnWeb user that maps to a Maxio customer.
/// </summary>
public class SubscriberProfile
{
    /// <summary>
    /// Stable, unique identifier used as the Maxio customer <c>reference</c>. The reference is the
    /// correlation key between eShopOnWeb and Maxio.
    /// </summary>
    public string Reference { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string? FirstName { get; init; }

    public string? LastName { get; init; }
}
