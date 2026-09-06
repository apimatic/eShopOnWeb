using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when subscription billing is invoked before it has been configured. Surfaced to API callers
/// as a service-unavailable response; the rest of the application keeps working without it.
/// </summary>
public class BillingNotConfiguredException : Exception
{
    public BillingNotConfiguredException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
