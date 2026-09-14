using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested subscription plan handle does not exist in the configured product family.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string message) : base(message)
    {
    }
}
