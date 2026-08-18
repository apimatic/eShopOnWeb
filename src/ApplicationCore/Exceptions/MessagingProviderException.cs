using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the messaging provider rejects or fails a request (a non-success HTTP response, a
/// transport failure, or an unparseable body). The message never contains the shopper's phone
/// number or any credential.
/// </summary>
public class MessagingProviderException : Exception
{
    public MessagingProviderException(string message, int? providerErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderErrorCode = providerErrorCode;
    }

    /// <summary>The provider's own error code, when it supplied one.</summary>
    public int? ProviderErrorCode { get; }
}
