using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MessagingProviderException : Exception
{
    public MessagingProviderException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
