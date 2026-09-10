using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription-billing operation fails. <see cref="StatusCode"/> carries the
/// caller-facing HTTP status the integration boundary already decided on, so the API layer can
/// surface a coherent, leak-free response: a caller-fixable fault (e.g. an unknown plan) maps to a
/// 4xx, while our-credentials / provider-down faults map to 5xx. The message is always caller-safe —
/// no SDK or framework exception text is ever placed in it.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code the API layer should return to the caller.</summary>
    public int StatusCode { get; }
}
