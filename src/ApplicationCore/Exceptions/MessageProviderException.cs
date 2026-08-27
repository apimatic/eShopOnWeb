using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The SMS provider rejected or failed an API call.
/// </summary>
public class MessageProviderException : Exception
{
    public MessageProviderException(string message, int? providerErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderErrorCode = providerErrorCode;
    }

    public int? ProviderErrorCode { get; }
}
