using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the billing integration raises. Everything the SDK can throw — API errors,
/// transport failures, unreadable bodies — is translated into this at the integration boundary, so
/// callers have one type to handle and no provider exception ever escapes.
/// </summary>
public class BillingException : Exception
{
    private static readonly IReadOnlyList<string> NoMessages = Array.Empty<string>();

    public BillingException(
        BillingFailureKind kind,
        string message,
        HttpStatusCode? providerStatusCode = null,
        IReadOnlyList<string>? providerMessages = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        ProviderMessages = providerMessages ?? NoMessages;
    }

    public BillingFailureKind Kind { get; }

    /// <summary>
    /// The status the billing system returned, when it is knowable. It is deliberately nullable: some
    /// provider errors arrive as a typed body whose status is implied by which accessor matched rather
    /// than reported, and an unreadable error body destroys the status altogether.
    /// </summary>
    public HttpStatusCode? ProviderStatusCode { get; }

    /// <summary>
    /// Validation messages the billing system returned verbatim. Safe to show a caller: these describe
    /// the request that was rejected, not our internals.
    /// </summary>
    public IReadOnlyList<string> ProviderMessages { get; }
}
