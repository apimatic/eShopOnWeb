using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Raised when the billing provider returns an error response for a well-formed request.
/// </summary>
public class MaxioApiException : Exception
{
    public int? StatusCode { get; }

    public MaxioApiException(string message, int? statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
