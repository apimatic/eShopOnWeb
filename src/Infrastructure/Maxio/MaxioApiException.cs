using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The Maxio API returned an error response. <see cref="StatusCode"/> is the HTTP status of
/// the upstream response and <see cref="Message"/> is a sanitized summary of its error body.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
