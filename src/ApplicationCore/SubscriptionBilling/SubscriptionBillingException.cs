using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Raised when the billing integration cannot satisfy a request. Carries the HTTP status
/// the API should surface to the caller.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, int statusCode = 500)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code that best represents this failure.</summary>
    public int StatusCode { get; }
}
