using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the provider could not be reached, timed out, or kept failing after the client
/// exhausted its retries. The call may or may not have been applied - re-read before retrying.
/// </summary>
public class BillingProviderUnavailableException : BillingProviderException
{
    public BillingProviderUnavailableException(string message, int? statusCode = null, IEnumerable<string>? providerErrors = null, Exception? innerException = null)
        : base(message, statusCode, providerErrors, innerException)
    {
    }
}
