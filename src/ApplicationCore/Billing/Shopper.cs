namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The authenticated eShopOnWeb identity used as the Maxio customer reference.
/// </summary>
public sealed record Shopper(string UserId, string Email, string UserName);
