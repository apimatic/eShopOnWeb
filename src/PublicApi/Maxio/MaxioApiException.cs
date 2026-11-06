using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A request that could not be carried out; carries the HTTP status the endpoint should answer with.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public MaxioApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
