using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(
        int statusCode,
        string message,
        string? code = null,
        string? logRef = null,
        string? resourceId = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        LogRef = logRef;
        ResourceId = resourceId;
    }

    public int StatusCode { get; }
    public string? Code { get; }
    public string? LogRef { get; }
    public string? ResourceId { get; }
}
