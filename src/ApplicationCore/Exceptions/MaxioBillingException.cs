using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioBillingException : Exception
{
    public MaxioBillingException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
