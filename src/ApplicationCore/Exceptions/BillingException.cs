using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum BillingErrorKind
{
    /// <summary>The caller's request is invalid (e.g. a plan handle the catalog does not offer).</summary>
    InvalidRequest = 1,

    /// <summary>The plan catalog was cut short at the page cap, so an unmatched plan cannot be called invalid.</summary>
    CatalogIncomplete = 2,

    /// <summary>The billing provider rejected the request with validation errors the caller can act on.</summary>
    ProviderRejected = 3,

    /// <summary>The billing provider failed, was unreachable, or refused our own credentials.</summary>
    ProviderUnavailable = 4,

    /// <summary>The billing provider did not answer within the request budget.</summary>
    ProviderTimeout = 5,

    /// <summary>The billing provider is throttling this application.</summary>
    ProviderThrottled = 6,

    /// <summary>A billing write may have landed but could not be confirmed yet.</summary>
    OutcomePending = 7
}

/// <summary>
/// A billing failure carrying only caller-safe text: never a credential, a raw provider body or an SDK message.
/// </summary>
public class BillingException : Exception
{
    public BillingException(BillingErrorKind kind, string message, IReadOnlyList<string>? providerErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderErrors = providerErrors ?? Array.Empty<string>();
    }

    public BillingErrorKind Kind { get; }

    /// <summary>Validation messages returned by the provider (for <see cref="BillingErrorKind.ProviderRejected"/>).</summary>
    public IReadOnlyList<string> ProviderErrors { get; }
}
