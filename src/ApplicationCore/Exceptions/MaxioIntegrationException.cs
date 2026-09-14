using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a Maxio Advanced Billing call fails or its response cannot be trusted.
/// </summary>
public class MaxioIntegrationException : Exception
{
    /// <summary>
    /// The HTTP status Maxio returned, when known. A 4xx means the caller's request was rejected
    /// (bad plan handle, validation failure, ...); null or 5xx means the provider/transport failed.
    /// </summary>
    public int? ProviderStatusCode { get; }

    public MaxioIntegrationException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }
}
