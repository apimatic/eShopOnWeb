using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised by the messaging provider when a call to it fails. The message is sanitised by the provider
/// implementation so it never carries a destination phone number or the auth secret.
/// </summary>
public class MessagingProviderException : Exception
{
    public int? ProviderErrorCode { get; }

    public MessagingProviderException(string sanitizedMessage, int? providerErrorCode = null)
        : base(sanitizedMessage)
    {
        ProviderErrorCode = providerErrorCode;
    }
}
