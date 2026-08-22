using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public PaymentException(string message, int statusCode, bool operatorActionable = false)
        : base(message)
    {
        StatusCode = statusCode;
        OperatorActionable = operatorActionable;
    }

    public PaymentException(string message, int statusCode, Exception inner, bool operatorActionable = false)
        : base(message, inner)
    {
        StatusCode = statusCode;
        OperatorActionable = operatorActionable;
    }

    public int StatusCode { get; }
    public bool OperatorActionable { get; }
}
