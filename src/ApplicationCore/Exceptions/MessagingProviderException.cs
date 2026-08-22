using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MessagingProviderException : Exception
{
    public int? StatusCode { get; }

    public MessagingProviderException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
