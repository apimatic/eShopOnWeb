using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation, string? providerMessage = null, Exception? innerException = null)
        : base(providerMessage is null
            ? $"Maxio request '{operation}' failed with HTTP {(int)statusCode}."
            : $"Maxio request '{operation}' failed: {providerMessage}", innerException)
    {
        StatusCode = statusCode;
        Operation = operation;
        ProviderMessage = providerMessage;
    }

    public HttpStatusCode StatusCode { get; }
    public string Operation { get; }
    public string? ProviderMessage { get; }
}
