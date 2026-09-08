namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// The eShopOnWeb identity of a shopper, carried into billing operations so that
/// the billing system can keep an idempotent, reference-based mapping to the user.
/// </summary>
public class BillingUserInfo
{
    /// <summary>
    /// The eShopOnWeb user id (stable, unique) - used as the billing customer reference.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}
