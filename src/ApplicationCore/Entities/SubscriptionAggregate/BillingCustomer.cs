namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user.
/// </summary>
/// <param name="Id">The provider-assigned numeric identifier.</param>
/// <param name="Reference">
/// The stable eShopOnWeb-side reference (the user's email / username). Creating a customer is
/// idempotent on this value, so a repeated subscribe attempt never produces a duplicate record.
/// </param>
/// <param name="Email">The customer's email address.</param>
/// <param name="FirstName">Given name as held by the provider.</param>
/// <param name="LastName">Family name as held by the provider.</param>
public record BillingCustomer(
    int Id,
    string Reference,
    string Email,
    string FirstName,
    string LastName);

/// <summary>
/// The details required to create a provider-side customer for an eShopOnWeb user.
/// </summary>
/// <param name="Reference">The stable eShopOnWeb-side reference (email / username).</param>
/// <param name="Email">Email address to register.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
public record BillingCustomerRegistration(
    string Reference,
    string Email,
    string FirstName,
    string LastName);
