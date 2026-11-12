using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The application cannot carry out the request. Carries the HTTP status to answer with and a reason for the caller.
/// Turned into a response by <see cref="Middleware.ExceptionMiddleware"/>.
/// </summary>
public class MaxioProblemException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioProblemException(HttpStatusCode statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static MaxioProblemException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    public static MaxioProblemException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    public static MaxioProblemException Conflict(string message) => new(HttpStatusCode.Conflict, message);
}

/// <summary>A paged read hit its cap, so the result would have been partial.</summary>
public class MaxioIncompleteResultException : MaxioProblemException
{
    public MaxioIncompleteResultException(string what)
        : base(HttpStatusCode.BadGateway, $"The {what} is too large to be read completely from the billing system; refusing to return a partial answer.")
    {
    }
}
