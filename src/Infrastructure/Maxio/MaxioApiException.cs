using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Billing API returns a non-success response or the request
/// cannot be completed.
/// </summary>
public class MaxioApiException : Exception
{
    public int? StatusCode { get; }

    public MaxioApiException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
