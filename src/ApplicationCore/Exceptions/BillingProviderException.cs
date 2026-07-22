using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider (Maxio) cannot fulfil a request — the provider is
/// unreachable, the credentials are rejected, or it returns a non-success response.
/// Mirrors the role of <see cref="DuplicateException"/> / <see cref="BasketNotFoundException"/>
/// as a domain-level, provider-agnostic error surfaced out of the single Infrastructure seam.
/// </summary>
public class BillingProviderException : Exception
{
    /// <summary>The HTTP status code returned by the provider, when the failure originated from a response.</summary>
    public int? StatusCode { get; }

    public BillingProviderException(string message) : base(message)
    {
    }

    public BillingProviderException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
