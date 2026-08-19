using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioApiException : Exception
{
    public MaxioApiException(string message, int statusCode, string? providerBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderBody = providerBody;
    }

    public int StatusCode { get; }
    public string? ProviderBody { get; }
}
