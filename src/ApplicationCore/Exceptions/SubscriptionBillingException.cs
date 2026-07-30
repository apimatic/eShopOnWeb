using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the subscription billing system (Maxio Advanced Billing) cannot fulfil a request,
/// e.g. an unknown plan, a validation error, or an upstream failure. Carries a suggested HTTP
/// status so the API surface can translate it into a meaningful response.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>Suggested HTTP status code for surfacing this failure to an API caller.</summary>
    public int StatusCode { get; }
}
