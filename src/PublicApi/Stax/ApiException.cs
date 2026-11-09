using System;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>An error that the API answers with the given HTTP status and a reason.</summary>
public class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static ApiException BadRequest(string m) => new(400, m);
    public static ApiException NotFound(string m) => new(404, m);
    public static ApiException Conflict(string m) => new(409, m);
    public static ApiException Unprocessable(string m) => new(422, m);
}

/// <summary>Stax answered with an error status.</summary>
public class StaxApiException : ApiException
{
    public int StaxStatus { get; }

    public StaxApiException(int staxStatus, string message)
        : base(staxStatus == 404 ? 404 : staxStatus >= 500 ? 502 : staxStatus == 401 || staxStatus == 403 ? 422 : staxStatus == 400 ? 400 : 422,
               $"Stax: {message}")
    {
        StaxStatus = staxStatus;
    }
}
