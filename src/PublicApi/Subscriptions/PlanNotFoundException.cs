using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string productHandle)
        : base($"No subscribable plan with handle '{productHandle}' exists in the configured product family.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
