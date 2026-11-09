using System;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>An error the API reports to its caller with a specific status and reason.</summary>
public class ApiException : Exception
{
    public int StatusCode { get; }
    public ApiException(int statusCode, string message) : base(message) { StatusCode = statusCode; }

    public static ApiException BadRequest(string m) => new(400, m);
    public static ApiException NotFound(string m) => new(404, m);
    public static ApiException Conflict(string m) => new(409, m);
    public static ApiException Unprocessable(string m) => new(422, m);
}

/// <summary>Stax answered with an error status (or could not be reached).</summary>
public class StaxException : ApiException
{
    public int StaxStatus { get; }

    public StaxException(int staxStatus, string message) : base(Map(staxStatus), message)
    {
        StaxStatus = staxStatus;
    }

    // Stax rejected the input -> 422 (404 stays 404); Stax or the network failing -> 502.
    private static int Map(int s) => s == 404 ? 404 : s is >= 400 and < 500 && s != 401 && s != 403 ? 422 : 502;
}
