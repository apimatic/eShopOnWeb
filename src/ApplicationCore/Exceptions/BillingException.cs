using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingException : Exception
{
    public BillingException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
