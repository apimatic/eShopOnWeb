using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to the billing provider failed. This is the single typed error the rest of eShopOnWeb sees:
/// the concrete provider client translates every transport, protocol and validation failure into it
/// (plan.md §4.2 — "normalizes results and throws typed errors").
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
    }

    public BillingProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public BillingProviderException(string operation, int? statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        StatusCode = statusCode;
    }

    /// <summary>The integration operation that failed, for example <c>create subscription</c>.</summary>
    public string? Operation { get; }

    /// <summary>The HTTP status the provider returned, when the failure reached the provider at all.</summary>
    public int? StatusCode { get; }
}
