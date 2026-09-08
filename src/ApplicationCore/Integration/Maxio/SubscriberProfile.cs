namespace Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;

/// <summary>
/// The shopper details used to provision a Maxio customer for an eShopOnWeb user.
/// </summary>
public class SubscriberProfile
{
    /// <summary>Stable unique customer reference owned by eShopOnWeb (its application-side customer id).</summary>
    public required string Reference { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
}
