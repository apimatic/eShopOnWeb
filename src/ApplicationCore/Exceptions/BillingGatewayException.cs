using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingGatewayException : Exception
{
    public BillingGatewayException(string message, int statusCode = 502, Exception innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
