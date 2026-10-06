using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle, string productFamilyHandle)
        : base($"Subscription plan '{planHandle}' was not found in product family '{productFamilyHandle}'")
    {
    }
}