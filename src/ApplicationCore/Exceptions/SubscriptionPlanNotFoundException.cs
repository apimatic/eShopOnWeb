using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle) : base($"No subscription plan with handle '{planHandle}' was found.")
    {
    }
}
