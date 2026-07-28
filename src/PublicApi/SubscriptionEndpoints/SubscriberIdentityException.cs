using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Thrown when an authenticated request carries no usable user identity claim, so no billing customer
/// can be resolved. Surfaces as HTTP 401.
/// </summary>
public class SubscriberIdentityException : Exception
{
    public SubscriberIdentityException()
        : base("The access token does not carry a user identity, so no subscription context could be resolved.")
    {
    }
}
