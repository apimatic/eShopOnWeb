using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type raised by the subscription billing boundary.
/// Every failure of the external billing provider - an API error, a transport failure,
/// a response that cannot be read - is translated into this type, so callers have one
/// thing to handle instead of a mix of provider-specific exceptions.
/// </summary>
/// <remarks>
/// <see cref="StatusCode"/> is the status this failure should be surfaced to *our* caller with;
/// <see cref="ProviderStatusCode"/> is the status the provider actually returned, kept for
/// diagnostics and for callers that need to distinguish a provider rejection from an outage.
/// The message is always caller-safe: provider or serializer exception text is never copied into it.
/// </remarks>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        string message,
        HttpStatusCode statusCode = HttpStatusCode.BadGateway,
        IReadOnlyList<string>? details = null,
        HttpStatusCode? providerStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderStatusCode = providerStatusCode;
        Details = details ?? Array.Empty<string>();
    }

    /// <summary>The HTTP status this failure should be reported to our own caller with.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The HTTP status the billing provider returned, when there was one.</summary>
    public HttpStatusCode? ProviderStatusCode { get; }

    /// <summary>Caller-safe validation messages returned by the provider, when there were any.</summary>
    public IReadOnlyList<string> Details { get; }
}
