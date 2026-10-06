using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message, string? rawBody)
        : base(message)
    {
        StatusCode = statusCode;
        RawBody = rawBody;
    }

    public int StatusCode { get; }

    public string? RawBody { get; }
}