using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioPlanNotFoundException : Exception
{
    public MaxioPlanNotFoundException(string planHandle) : base($"No subscription plan was found with handle '{planHandle}'.")
    {
    }
}
