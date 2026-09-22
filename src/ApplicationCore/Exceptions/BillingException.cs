using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Classifies a billing failure so the transport layer can map it to a caller-facing status
/// without knowing anything about the underlying billing provider.
/// </summary>
public enum BillingErrorKind
{
    /// <summary>The caller's request was rejected as invalid (maps to 400).</summary>
    InvalidRequest,

    /// <summary>The requested resource was not found (maps to 404).</summary>
    NotFound,

    /// <summary>Our credentials/quota, the provider being down, or a transport failure (maps to 503).</summary>
    ProviderUnavailable,

    /// <summary>An outcome we could not classify — never blamed on the caller (maps to 502).</summary>
    Unknown
}

/// <summary>
/// A billing operation failed. Carries a <see cref="Kind"/> the boundary maps to an HTTP status
/// and a caller-safe message; never surfaces provider/SDK exception text on the wire.
/// </summary>
public class BillingException : Exception
{
    public BillingErrorKind Kind { get; }

    public BillingException(string message, BillingErrorKind kind, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }
}
