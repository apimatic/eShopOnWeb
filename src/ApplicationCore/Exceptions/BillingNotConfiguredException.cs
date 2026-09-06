using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription endpoint is reached but this deployment has no billing configuration.
/// Surfaced as 503 so the deficiency is unmistakably operational rather than a caller mistake.
/// </summary>
public class BillingNotConfiguredException : Exception
{
    public BillingNotConfiguredException(string message) : base(message)
    {
    }
}
