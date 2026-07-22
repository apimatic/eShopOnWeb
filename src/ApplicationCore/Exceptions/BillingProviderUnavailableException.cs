using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider could not be reached at all — a connection failure, a
/// timeout, or a server-side error. The request may or may not have been applied, so callers
/// must re-read provider state before retrying a write.
/// </summary>
public class BillingProviderUnavailableException : BillingProviderException
{
    public BillingProviderUnavailableException(string operation, string message, int? statusCode = null, Exception? innerException = null)
        : base(operation, message, statusCode, innerException)
    {
    }
}
