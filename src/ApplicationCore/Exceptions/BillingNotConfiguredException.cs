using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscription-billing operation is invoked on a deployment that has no billing
/// provider configured. The rest of the API keeps serving; only this capability is unavailable.
/// </summary>
public class BillingNotConfiguredException : Exception
{
    public BillingNotConfiguredException(string message) : base(message)
    {
    }
}
