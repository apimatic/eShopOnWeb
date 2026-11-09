using System;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Thrown when the app can't carry out a request. The exception middleware turns it into
/// a response (usually 4xx) carrying the reason.
/// </summary>
public class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static ApiException BadRequest(string message) => new(400, message);
    public static ApiException Unauthorized(string message) => new(401, message);
    public static ApiException NotFound(string message) => new(404, message);
    public static ApiException Conflict(string message) => new(409, message);
    public static ApiException Unprocessable(string message) => new(422, message);
}
