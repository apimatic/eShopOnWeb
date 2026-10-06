using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A failure raised by the Maxio Advanced Billing integration. The message is always safe
/// to expose to API callers; the status code is the client-facing HTTP status.
/// </summary>
public class MaxioIntegrationException : Exception
{
    public int StatusCode { get; }

    public MaxioIntegrationException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}