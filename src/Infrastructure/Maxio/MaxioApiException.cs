using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success response.
/// </summary>
internal class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public MaxioApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}