using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Reads who is calling from the JWT.</summary>
public static class Caller
{
    /// <summary>The signed-in user's name (the JWT name claim), which is also the reference of their Maxio customer.</summary>
    public static string UserName(ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new MaxioProblemException(System.Net.HttpStatusCode.Unauthorized, "The token does not identify a user.");
        return name;
    }
}

/// <summary>Request that acts on one of the caller's subscriptions.</summary>
public class SubscriptionActionRequest : BaseRequest
{
    public SubscriptionActionRequest(string userName, int subscriptionId)
    {
        UserName = userName;
        SubscriptionId = subscriptionId;
    }

    public string UserName { get; }
    public int SubscriptionId { get; }
}
