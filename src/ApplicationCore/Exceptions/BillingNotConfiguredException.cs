using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing integration is not usable because its configuration is missing or invalid.
/// </summary>
/// <remarks>
/// Reported as 503 so the capability degrades on its own rather than taking the whole API down at startup:
/// a deployment that does not use subscription billing still serves the catalog endpoints normally.
/// </remarks>
public sealed class BillingNotConfiguredException : BillingException
{
    public BillingNotConfiguredException(string message, Exception? innerException = null)
        : base(message, 503, innerException)
    {
    }
}
