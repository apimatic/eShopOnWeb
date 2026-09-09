using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A failure raised by the Maxio Advanced Billing integration boundary.
/// The message is always safe to surface to API callers: it never contains
/// SDK exception details, raw provider bodies, or credentials.
/// </summary>
public sealed class MaxioBillingException : Exception
{
    public MaxioBillingException(int? providerStatusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        RecommendedHttpStatusCode = providerStatusCode switch
        {
            404 => 404,
            _ => 502
        };
    }

    /// <summary>The HTTP status the billing provider returned, when known.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>The HTTP status this integration recommends surfacing to its own callers.</summary>
    public int RecommendedHttpStatusCode { get; }
}
