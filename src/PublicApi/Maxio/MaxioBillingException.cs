using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Single failure type for the Maxio integration boundary. Carries the provider's
/// HTTP status when one exists so callers can distinguish a client-correctable
/// rejection (4xx) from a provider/transport failure (5xx).
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(string message, HttpStatusCode? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>The status Maxio returned, when the failure was an API error response.</summary>
    public HttpStatusCode? ProviderStatusCode { get; }

    /// <summary>True when Maxio actively rejected the request with a 4xx (caller can act on it).</summary>
    public bool IsProviderRejection => ProviderStatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError;
}
