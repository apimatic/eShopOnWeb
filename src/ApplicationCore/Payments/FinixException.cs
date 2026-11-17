using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class FinixException : Exception
{
    public FinixException(int statusCode, string? errorCode, string message, string? authorizationId = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        AuthorizationId = authorizationId;
    }

    public int StatusCode { get; }
    public string? ErrorCode { get; }
    public string? AuthorizationId { get; }
}
