namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The eShopOnWeb identity a billing operation acts for. <paramref name="UserId"/>
/// becomes the Maxio customer <c>reference</c> (server-enforced unique), which is
/// what makes customer creation idempotent.
/// </summary>
public sealed record BillingUser(string UserId, string Email, string FirstName, string LastName);
