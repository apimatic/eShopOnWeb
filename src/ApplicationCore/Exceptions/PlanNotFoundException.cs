using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a caller references a subscription plan handle that does not exist in the
/// configured product family. Surfaces as HTTP 404.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' exists in the configured product family.")
    {
    }
}
