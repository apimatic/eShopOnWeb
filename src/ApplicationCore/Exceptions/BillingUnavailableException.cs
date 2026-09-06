using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider could not be reached, timed out, or failed on its side. The request had
/// no effect (or, for reads, simply produced nothing) and may safely be retried.
/// </summary>
public class BillingUnavailableException : BillingException
{
    public BillingUnavailableException(string message, Exception? innerException = null, int? providerStatusCode = null)
        : base(message, innerException, providerStatusCode)
    {
    }
}
