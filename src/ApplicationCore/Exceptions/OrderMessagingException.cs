using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderMessagingException : Exception
{
    public OrderMessagingException(string message, int? httpStatus = null, Exception? inner = null)
        : base(message, inner)
    {
        HttpStatus = httpStatus;
    }

    public int? HttpStatus { get; }

    public bool IsCallerFault =>
        HttpStatus is >= 400 and < 500
        && HttpStatus is not 401 and not 403 and not 429;
}
