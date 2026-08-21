using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when Maxio Advanced Billing rejects a request or is unreachable.
/// </summary>
public class BillingGatewayException : Exception
{
    public BillingGatewayException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingGatewayException(string message, Exception innerException, int? statusCode = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
