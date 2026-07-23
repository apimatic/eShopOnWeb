namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseMessage
{
    /// <summary>The caller, taken from the bearer token.</summary>
    public string? UserReference { get; private set; }

    public void SetUserReference(string? userReference) => UserReference = userReference;
}
