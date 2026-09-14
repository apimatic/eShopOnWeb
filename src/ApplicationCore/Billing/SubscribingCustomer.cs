namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The eShopOnWeb identity of the shopper being enrolled. <see cref="UserId"/> is the app's own
/// (stable) identity key and is used as the Maxio customer's idempotency reference - never the
/// username/email, which could change.
/// </summary>
public class SubscribingCustomer
{
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}
