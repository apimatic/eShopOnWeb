namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity of the caller, resolved from the JWT at the endpoint boundary and
/// passed into the billing service. <see cref="UserId"/> is the stable key used to correlate this
/// shopper with a Maxio customer record (as the customer <c>reference</c>), so it must not change
/// for a given user — the eShop <c>ApplicationUser.Id</c> (a GUID) is used.
/// </summary>
public record BillingAppUser(string UserId, string Email, string FirstName, string LastName);
