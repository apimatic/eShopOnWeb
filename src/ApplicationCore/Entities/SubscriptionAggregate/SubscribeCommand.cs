namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Command describing a logged-in shopper subscribing to a plan.
/// </summary>
public class SubscribeCommand
{
    public SubscribeCommand(string userId, string email, string productHandle)
    {
        UserId = userId;
        Email = email;
        ProductHandle = productHandle;
    }

    /// <summary>
    /// The stable eShopOnWeb user identifier; used as the Maxio customer reference.
    /// </summary>
    public string UserId { get; }
    public string Email { get; }
    /// <summary>
    /// The Maxio product handle of the plan to subscribe to.
    /// </summary>
    public string ProductHandle { get; }
}
