using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the billing provider rejects or cannot serve a request. This is the single typed
/// error the provider seam surfaces: every provider SDK exception and every transport failure is
/// converted into this type so that no provider-specific exception ever escapes Infrastructure.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string operation, string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base($"Billing provider call '{operation}' failed: {message}", innerException)
    {
        Operation = operation;
        ProviderMessage = message;
        StatusCode = statusCode;
    }

    /// <summary>The seam operation that failed, e.g. <c>CreateSubscription</c>.</summary>
    public string Operation { get; }

    /// <summary>The provider's own message, safe to surface to an operator.</summary>
    public string ProviderMessage { get; }

    /// <summary>The HTTP status the provider returned, when one was available.</summary>
    public HttpStatusCode? StatusCode { get; }
}
