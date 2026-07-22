namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user. <paramref name="Reference"/> carries the
/// eShopOnWeb identity (the signed in user name) and is what makes customer creation idempotent.
/// </summary>
public sealed record BillingCustomer(
    int Id,
    string Reference,
    string Email,
    string FirstName,
    string LastName);
