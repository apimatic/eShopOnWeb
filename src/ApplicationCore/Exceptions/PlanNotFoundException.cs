using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string productHandle)
        : base($"No subscription plan with handle '{productHandle}' is available.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
