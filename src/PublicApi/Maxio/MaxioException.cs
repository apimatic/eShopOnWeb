using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A failure to carry out a request; carries the HTTP status the endpoint should answer with.
/// </summary>
public class MaxioException : Exception
{
    public int StatusCode { get; }

    public MaxioException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
