namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// The eShopOnWeb shopper as the billing layer needs to see them. <see cref="Reference"/> is the
/// stable per-shopper key written to the Maxio customer's <c>reference</c> field — it is what makes
/// customer creation idempotent (look up by reference before creating).
/// </summary>
public record ShopperIdentity(string Reference, string Email, string FirstName, string LastName);
