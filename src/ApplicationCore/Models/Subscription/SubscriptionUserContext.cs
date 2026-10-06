namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// Identifies the eShopOnWeb user whose billing records are being managed.
/// </summary>
public class SubscriptionUserContext
{
    public SubscriptionUserContext(string userId, string userName, string email)
    {
        UserId = userId;
        UserName = userName;
        Email = email;
    }

    /// <summary>
    /// The stable ASP.NET Identity user id; used as the Maxio customer reference.
    /// </summary>
    public string UserId { get; }

    public string UserName { get; }

    public string Email { get; }
}