using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Raised when Maxio Advanced Billing returns an error response that the caller must
/// surface (validation failures, unreachable/misconfigured site, unexpected status codes).
/// </summary>
public class MaxioApiException : Exception
{
    public int? StatusCode { get; }

    public MaxioApiException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }

    public MaxioApiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
