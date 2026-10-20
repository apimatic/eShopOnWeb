using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// The single failure type the billing integration raises. <see cref="StatusCode"/> is the status the
/// caller of PublicApi receives, and <see cref="Exception.Message"/> is safe to show them: it never carries
/// SDK type names, URLs or credentials.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public HttpStatusCode StatusCode { get; }

    /// <summary>The HTTP status Maxio answered with, when it answered at all.</summary>
    public HttpStatusCode? ProviderStatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public SubscriptionBillingException(HttpStatusCode statusCode, string message,
        HttpStatusCode? providerStatusCode = null, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderStatusCode = providerStatusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public static SubscriptionBillingException Unavailable(string message, HttpStatusCode? providerStatusCode = null, Exception? innerException = null) =>
        new(HttpStatusCode.BadGateway, message, providerStatusCode, null, innerException);
}
