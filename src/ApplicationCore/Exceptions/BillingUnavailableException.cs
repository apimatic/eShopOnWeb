using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The billing provider could not be used (unreachable, misconfigured, or failing); nothing was changed.</summary>
public class BillingUnavailableException : Exception
{
    public BillingUnavailableException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}
