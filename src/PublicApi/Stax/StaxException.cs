using System;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>Raised when Stax refuses a request or cannot be reached.</summary>
public class StaxException : Exception
{
    public StaxException(int statusCode, string message) : base(message) => StatusCode = statusCode;

    /// <summary>The HTTP status Stax answered with (0 when it could not be reached).</summary>
    public int StatusCode { get; }

    /// <summary>True when Stax rejected the request as invalid (as opposed to being broken or unreachable).</summary>
    public bool IsRejection => StatusCode >= 400 && StatusCode < 500 && StatusCode != 401 && StatusCode != 403 && StatusCode != 429;
}

/// <summary>A request the app cannot carry out; answered with the status code and the message as the reason.</summary>
public class ApiRequestException : Exception
{
    public ApiRequestException(int statusCode, string message) : base(message) => StatusCode = statusCode;
    public int StatusCode { get; }
}
