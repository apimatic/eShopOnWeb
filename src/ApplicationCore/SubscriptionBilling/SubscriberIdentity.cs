namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// The identity of the shopper making a subscription request, taken from the caller's JWT.
/// <see cref="BuyerId"/> is the stable per-user key used as the Maxio <c>customer.reference</c>.
/// </summary>
public record SubscriberIdentity
{
    public required string BuyerId { get; init; }
    public required string Email { get; init; }
}
