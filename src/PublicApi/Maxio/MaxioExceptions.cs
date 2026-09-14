using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Base type for failures raised by the Maxio subscription integration. Carries the HTTP
/// status that callers should surface so provider 4xx rejections stay 4xx instead of being
/// collapsed into a generic server error.
/// </summary>
public abstract class MaxioException : Exception
{
    protected MaxioException(HttpStatusCode statusCode, string message, Exception? innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

/// <summary>
/// Maxio answered with a non-success response (or the app rejected the request based on a
/// Maxio response). The status is meaningful to the caller.
/// </summary>
public sealed class MaxioApiException : MaxioException
{
    public MaxioApiException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(statusCode, message, innerException)
    {
    }
}

/// <summary>
/// Maxio could not be reached or did not answer in time. The outcome of any in-flight write
/// is unknown.
/// </summary>
public sealed class MaxioUnavailableException : MaxioException
{
    public MaxioUnavailableException(HttpStatusCode statusCode, string message, Exception? innerException)
        : base(statusCode, message, innerException)
    {
    }
}

/// <summary>
/// The Maxio integration is not usable because of a local configuration problem. Surface as a
/// server error; it will not resolve itself without a configuration change.
/// </summary>
public sealed class MaxioConfigurationException : MaxioException
{
    public MaxioConfigurationException(string message)
        : base(HttpStatusCode.InternalServerError, message, innerException: null)
    {
    }
}
