using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Thrown when the shop cannot carry out a request. The exception middleware answers with
/// <see cref="StatusCode"/> and the message as the reason.
/// </summary>
public class AppException : Exception
{
    public AppException(HttpStatusCode statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>The HTTP status Stax answered with, when the failure came from Stax.</summary>
    public int? StaxStatus { get; init; }

    /// <summary>The reason Stax gave, when the failure came from Stax.</summary>
    public string? StaxMessage { get; init; }

    public static AppException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    public static AppException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    public static AppException Conflict(string message) => new(HttpStatusCode.Conflict, message);
    public static AppException Unprocessable(string message) => new(HttpStatusCode.UnprocessableEntity, message);
}
