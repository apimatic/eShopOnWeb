using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingGatewayException : Exception
{
    public BillingGatewayException(string message, int statusCode, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }
    public string? ResponseBody { get; }
}
