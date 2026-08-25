using System;

namespace Microsoft.eShopWeb.PublicApi.Services;

/// <summary>
/// Caller-safe failure of the Maxio billing integration. Carries the HTTP status the API
/// should surface: provider 4xx are carried through, transport/unknown failures are 5xx.
/// </summary>
public class MaxioIntegrationException : Exception
{
    public MaxioIntegrationException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
