using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure talking to the SMS provider. The message deliberately excludes
/// destination phone numbers and credentials so it is safe to log.
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
