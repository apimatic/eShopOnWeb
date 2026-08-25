using System;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class PaymentException : Exception
{
    public int? HttpStatus { get; }

    public PaymentException(string message, int? httpStatus = null, Exception? inner = null)
        : base(message, inner)
    {
        HttpStatus = httpStatus;
    }
}
