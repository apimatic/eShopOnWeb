using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the PayPal integration boundary raises. The Infrastructure gateway converts every
/// SDK failure — API errors, transport failures and malformed responses — into this type, carrying a
/// caller-safe message plus the provider's HTTP status (when known) so the API layer can map a provider 4xx
/// back to a client 4xx and an outage to a 5xx, rather than collapsing everything into one status.
/// </summary>
public class PayPalException : Exception
{
    public PayPalException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    /// <summary>The provider's HTTP status code, when the failure came from a provider response.</summary>
    public int? StatusCode { get; }

    /// <summary>
    /// True when the provider rejected the request with a client-actionable status (a 4xx). A capture that
    /// fails this way against a stale hold is the signal to try renewing the authorization.
    /// </summary>
    public bool IsProviderRejection => StatusCode is >= 400 and < 500;
}
