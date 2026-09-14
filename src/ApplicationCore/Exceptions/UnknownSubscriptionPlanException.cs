using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string planHandle)
        : base($"'{planHandle}' is not an active subscription plan.")
    {
    }
}
