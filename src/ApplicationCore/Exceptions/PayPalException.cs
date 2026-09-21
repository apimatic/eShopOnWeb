using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure talking to PayPal, translated at the gateway boundary into a single domain type so
/// callers handle one failure kind. Carries a caller-safe <see cref="Exception.Message"/>, and,
/// where available, the HTTP status and PayPal's own correlation id (<see cref="DebugId"/>) and
/// error name for an operator to act on. Never carries card data.
/// </summary>
public class PayPalException : Exception
{
    public PayPalException(string message, HttpStatusCode? statusCode = null,
        string? debugId = null, string? providerErrorName = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        ProviderErrorName = providerErrorName;
    }

    /// <summary>HTTP status PayPal returned, when the failure carried one.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>PayPal's <c>debug_id</c> correlation id, for support/reconciliation.</summary>
    public string? DebugId { get; }

    /// <summary>PayPal's machine error name (e.g. INSTRUMENT_DECLINED), when present.</summary>
    public string? ProviderErrorName { get; }
}

/// <summary>
/// Raised when PayPal requires a browser approval (a challenge / payer-action) to proceed with a
/// card payment. The task mandates stopping and reporting rather than building an approval
/// round-trip, so this surfaces to the operator as an actionable, distinct failure.
/// </summary>
public class PayPalChallengeException : PayPalException
{
    public PayPalChallengeException(string message)
        : base(message)
    {
    }
}
