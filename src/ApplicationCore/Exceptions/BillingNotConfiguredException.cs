using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Subscription billing was asked for but the application has no billing credentials configured.
/// The rest of eShopOnWeb keeps working; only the subscription endpoints report unavailable.
/// </summary>
public class BillingNotConfiguredException : Exception
{
    public BillingNotConfiguredException(string message) : base(message)
    {
    }
}
