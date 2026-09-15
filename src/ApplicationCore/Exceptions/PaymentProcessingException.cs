using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentProcessingException : Exception
{
    public PaymentProcessingException(string message, int statusCode = 502, bool operatorActionable = false)
        : base(message)
    {
        StatusCode = statusCode;
        OperatorActionable = operatorActionable;
    }

    public PaymentProcessingException(string message, Exception inner, int statusCode = 502, bool operatorActionable = false)
        : base(message, inner)
    {
        StatusCode = statusCode;
        OperatorActionable = operatorActionable;
    }

    public int StatusCode { get; }
    public bool OperatorActionable { get; }
}
