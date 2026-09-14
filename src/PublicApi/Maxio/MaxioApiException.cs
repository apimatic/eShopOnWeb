using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing API responds with an error status that
/// the caller did not opt into handling (for example a lookup returning 404).
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>The HTTP status code returned by Maxio.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The raw response body returned by Maxio (when available).</summary>
    public string? ResponseBody { get; }
}

/// <summary>
/// Thrown when the Maxio Advanced Billing API could not be reached or answered in time
/// (DNS/TLS/connect failure, timeout). Distinct from <see cref="MaxioApiException"/>,
/// which indicates Maxio answered with an error status.
/// </summary>
public sealed class MaxioUnavailableException : Exception
{
    public MaxioUnavailableException(string message)
        : base(message)
    {
    }

    public MaxioUnavailableException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
