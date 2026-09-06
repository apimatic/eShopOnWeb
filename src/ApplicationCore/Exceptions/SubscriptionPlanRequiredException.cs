using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscribe request names no plan and the deployment configures no default one.
/// </summary>
public class SubscriptionPlanRequiredException : Exception
{
    public SubscriptionPlanRequiredException(string message) : base(message)
    {
    }
}
