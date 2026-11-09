using System;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Thrown when the API cannot carry out a request. The exception middleware turns it into the given
/// HTTP status with the message as the reason.
/// </summary>
public class ApiProblemException : Exception
{
    public int StatusCode { get; }

    public ApiProblemException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static ApiProblemException BadRequest(string message) => new(400, message);
    public static ApiProblemException NotFound(string message) => new(404, message);
    public static ApiProblemException Conflict(string message) => new(409, message);
    public static ApiProblemException Unprocessable(string message) => new(422, message);
}
