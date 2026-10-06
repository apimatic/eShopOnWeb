using System.Net;

namespace Maxio.Exceptions;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public string? ResponseBody { get; }

    /// <summary>
    /// Human-readable error messages extracted from the Maxio error response body, when present.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(HttpStatusCode statusCode, string? responseBody, IReadOnlyList<string> errors, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = errors;
    }
}
