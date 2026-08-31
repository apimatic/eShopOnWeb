using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The SMS provider rejected or failed an API call. Carries the provider's own
/// error code when one was returned.
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
