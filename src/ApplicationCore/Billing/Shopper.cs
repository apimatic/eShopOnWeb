namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The authenticated eShopOnWeb user that maps 1:1 to a Maxio customer via <see cref="UserId"/>.
/// </summary>
public sealed record Shopper(string UserId, string Email, string UserName);
