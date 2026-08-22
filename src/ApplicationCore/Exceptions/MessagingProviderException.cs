using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MessagingProviderException : Exception
{
    public MessagingProviderException(string message, int? httpStatus = null, Exception? inner = null)
        : base(message, inner)
    {
        HttpStatus = httpStatus;
    }

    public int? HttpStatus { get; }
}
