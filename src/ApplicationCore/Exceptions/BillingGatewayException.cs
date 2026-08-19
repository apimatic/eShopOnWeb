using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingGatewayException : BillingException
{
    public BillingGatewayException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingGatewayException(string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
