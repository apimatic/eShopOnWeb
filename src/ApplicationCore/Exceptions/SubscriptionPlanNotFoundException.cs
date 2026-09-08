using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscription is requested for a plan handle that does not exist
/// on the configured Maxio product family.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle) : base($"No subscription plan with handle '{planHandle}' exists.")
    {
    }
}
