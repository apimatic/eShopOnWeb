using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when a requested plan handle does not exist in the configured Maxio product family.
/// </summary>
public class MaxioPlanNotFoundException : Exception
{
    public MaxioPlanNotFoundException(string planHandle)
        : base($"No subscription plan was found with handle '{planHandle}'.")
    {
    }
}
