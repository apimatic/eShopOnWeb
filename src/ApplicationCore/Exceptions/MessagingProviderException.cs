using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to the messaging provider failed. The message deliberately excludes
/// provider response detail, which can embed destination phone numbers.
/// </summary>
public class MessagingProviderException : Exception
{
    public MessagingProviderException(int httpStatusCode, int? providerErrorCode, string operation)
        : base($"Messaging provider {operation} failed with HTTP {httpStatusCode}.")
    {
        HttpStatusCode = httpStatusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public MessagingProviderException(string operation, Exception innerException)
        : base($"Messaging provider {operation} failed.", innerException)
    {
    }

    public int? HttpStatusCode { get; }
    public int? ProviderErrorCode { get; }
}
