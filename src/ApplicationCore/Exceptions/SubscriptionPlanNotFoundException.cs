using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscription is requested for a plan that does not exist
/// (or is not part of the configured product family).
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string handle)
        : base($"Subscription plan '{handle}' was not found.")
    {
    }
}