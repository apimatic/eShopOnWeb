using System;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>An error the API answers with a specific HTTP status and a reason.</summary>
public class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static ApiException BadRequest(string message) => new(400, message);
    public static ApiException NotFound(string message) => new(404, message);
    public static ApiException Conflict(string message) => new(409, message);
    public static ApiException Unprocessable(string message) => new(422, message);
}
