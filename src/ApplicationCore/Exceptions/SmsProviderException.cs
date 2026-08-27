using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the messaging provider rejects a request or is unreachable.
/// </summary>
public class SmsProviderException : Exception
{
    public int? ProviderErrorCode { get; }

    public SmsProviderException(string message, int? providerErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderErrorCode = providerErrorCode;
    }
}
