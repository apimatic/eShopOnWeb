namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The billing-relevant identity of the current eShopOnWeb user. Reference is the
/// stable identity user id, used as the Maxio customer reference.
/// </summary>
public sealed record BillingCustomerContext(string Reference, string Email, string FirstName, string LastName);
