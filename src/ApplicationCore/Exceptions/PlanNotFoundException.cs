using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string planHandle)
        : base($"A subscription plan with handle '{planHandle}' was not found in the billing catalog.")
    {
    }
}
