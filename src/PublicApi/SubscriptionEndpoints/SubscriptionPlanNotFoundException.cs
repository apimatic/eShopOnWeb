using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Thrown when a shopper asks to subscribe to a plan handle that is not part of the
/// configured product family (or does not exist on the Maxio site).
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string message) : base(message)
    {
    }
}
