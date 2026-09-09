using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription-billing operation cannot be completed. Carries an HTTP status
/// code so the API layer can translate it into an appropriate response:
/// - 400 for invalid caller input (e.g. an unknown plan handle),
/// - 502 for an upstream billing-system failure.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }

    public static SubscriptionBillingException BadRequest(string message) =>
        new(message, 400);

    public static SubscriptionBillingException UpstreamFailure(string message, Exception? inner = null) =>
        new(message, 502, inner);
}
