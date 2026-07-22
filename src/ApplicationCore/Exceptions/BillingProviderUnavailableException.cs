using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing provider could not be reached, timed out, or answered with a server side failure.
/// The requested change may or may not have been applied — callers must re-read state before retrying.
/// </summary>
public class BillingProviderUnavailableException : BillingProviderException
{
    public BillingProviderUnavailableException(string message, string operation, int? providerStatusCode = null,
        IReadOnlyList<string>? providerErrors = null, Exception? innerException = null)
        : base(message, operation, providerStatusCode, providerErrors, innerException)
    {
    }
}
