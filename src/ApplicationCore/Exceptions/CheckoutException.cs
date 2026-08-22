using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CheckoutException : Exception
{
    public CheckoutException(string message, int statusCode, bool operatorActionable = false)
        : base(message)
    {
        StatusCode = statusCode;
        OperatorActionable = operatorActionable;
    }

    public int StatusCode { get; }
    public bool OperatorActionable { get; }
}
