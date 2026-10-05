using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when subscribing to a plan handle that does not exist in the configured
/// Maxio product family.
/// </summary>
public class UnknownPlanException : Exception
{
    public UnknownPlanException(string message) : base(message)
    {
    }
}