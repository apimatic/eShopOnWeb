using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalOperationException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public bool IsOperatorActionable { get; }

    public PayPalOperationException(string message, HttpStatusCode statusCode, bool isOperatorActionable = false)
        : base(message)
    {
        StatusCode = statusCode;
        IsOperatorActionable = isOperatorActionable;
    }

    public PayPalOperationException(string message, HttpStatusCode statusCode, Exception inner, bool isOperatorActionable = false)
        : base(message, inner)
    {
        StatusCode = statusCode;
        IsOperatorActionable = isOperatorActionable;
    }
}
