using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The caller is authenticated but cannot be turned into a billable shopper — an orphaned token, or
/// an account with no email address. Carries the HTTP status the API should answer with.
/// </summary>
public class SubscriberResolutionException : Exception
{
    public SubscriberResolutionException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
