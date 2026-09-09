using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle, string productFamilyHandle)
        : base($"No subscription plan with handle '{productHandle}' exists in product family '{productFamilyHandle}'.")
    {
    }
}
