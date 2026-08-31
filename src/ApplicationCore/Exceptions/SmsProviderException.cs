using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the messaging provider rejects a request or cannot be reached.
/// Carries the provider's error code when one was supplied.
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message, int? providerErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderErrorCode = providerErrorCode;
    }

    public int? ProviderErrorCode { get; }
}
