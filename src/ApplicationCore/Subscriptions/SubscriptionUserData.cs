namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb user for subscription operations.
/// </summary>
public class SubscriptionUserData
{
    /// <summary>
    /// The ASP.NET Identity user id; used as the stable reference key in the billing system.
    /// </summary>
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}