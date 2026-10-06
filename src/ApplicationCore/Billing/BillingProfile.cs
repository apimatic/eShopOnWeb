namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The shopper's identity as it is handed to the billing system. <see cref="Reference"/> is
/// the application-owned key that makes customer creation idempotent: Maxio enforces that
/// only one customer can exist for a given reference value.
/// </summary>
/// <param name="Reference">Unique identifier of the eShopOnWeb account (e.g. "eshop-demouser@microsoft.com").</param>
/// <param name="FirstName">Customer first name (required by Maxio when creating a customer).</param>
/// <param name="LastName">Customer last name (required by Maxio when creating a customer).</param>
/// <param name="Email">Customer email (required by Maxio when creating a customer).</param>
public record BillingProfile(string Reference, string FirstName, string LastName, string Email);
