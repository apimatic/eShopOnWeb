using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>An error that is reported to the caller as an HTTP status with a reason.</summary>
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

/// <summary>Stax answered with an error (or could not be reached).</summary>
public class StaxException : ApiException
{
    /// <summary>The status Stax answered with; 0 when Stax could not be reached.</summary>
    public int UpstreamStatus { get; }

    public StaxException(int upstreamStatus, string message)
        : base(MapStatus(upstreamStatus), message)
    {
        UpstreamStatus = upstreamStatus;
    }

    // Stax rejecting the request because of its content is the caller's 4xx; Stax failing
    // or rejecting our credentials is not something the caller can fix.
    private static int MapStatus(int upstream) => upstream switch
    {
        400 => 400,
        404 => 404,
        409 => 409,
        422 => 422,
        429 => 429,
        401 or 403 => 502,
        >= 400 and < 500 => 422,
        _ => 502
    };
}
