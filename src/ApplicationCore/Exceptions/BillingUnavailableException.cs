using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system could not be reached, timed out, throttled the caller, or failed on its side.
/// The request may or may not have been applied, so it is only safe to retry with the same
/// uniqueness token.
/// </summary>
public class BillingUnavailableException : BillingException
{
    public BillingUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
