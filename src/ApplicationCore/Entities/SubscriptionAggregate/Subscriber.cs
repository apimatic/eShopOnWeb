namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The eShopOnWeb identity being billed. Built from the authenticated caller, never from
/// request input, so a caller can only ever act on their own billing account.
/// </summary>
public class Subscriber
{
    /// <summary>The eShopOnWeb identity user id.</summary>
    public required string UserId { get; init; }

    /// <summary>The eShopOnWeb user name (an email address in eShopOnWeb).</summary>
    public required string UserName { get; init; }

    /// <summary>The email address the billing provider should use for this customer.</summary>
    public required string Email { get; init; }

    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
