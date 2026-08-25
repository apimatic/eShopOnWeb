using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The single failure type leaving the Maxio integration boundary.
/// Carries the provider's HTTP status so callers can distinguish a
/// client-actionable 4xx from a provider/transport 5xx. The message is
/// always caller-safe (no SDK internals, no raw exception text).
/// </summary>
public class MaxioBillingException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioBillingException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
