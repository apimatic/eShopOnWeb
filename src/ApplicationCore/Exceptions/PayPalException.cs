using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PayPalException : Exception
{
    public int? HttpStatus { get; }

    public PayPalException(string message, Exception? inner = null, int? httpStatus = null)
        : base(message, inner)
    {
        HttpStatus = httpStatus;
    }
}
