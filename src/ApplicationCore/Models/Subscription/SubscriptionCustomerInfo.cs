namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// Identity information about the signed-in eShopOnWeb shopper used to
/// map them onto a billing-system customer.
/// </summary>
public class SubscriptionCustomerInfo
{
    public SubscriptionCustomerInfo(string userId, string userName, string email)
    {
        UserId = userId;
        UserName = userName;
        Email = email;
    }

    /// <summary>Stable eShopOnWeb user id; used as the billing customer reference.</summary>
    public string UserId { get; }
    public string UserName { get; }
    public string Email { get; }
}
