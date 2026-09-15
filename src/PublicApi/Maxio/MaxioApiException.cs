using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string operation)
        : base($"Maxio returned HTTP {statusCode} while attempting to {operation}.")
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
