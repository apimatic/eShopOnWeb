using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found in the configured Maxio product family.")
    {
    }
}

public class SubscriptionEnrollmentInProgressException : Exception
{
    public SubscriptionEnrollmentInProgressException()
        : base("This subscription enrollment is already in progress. Retry shortly to receive its result.")
    {
    }
}

public class SubscriptionConsistencyException : Exception
{
    public SubscriptionConsistencyException()
        : base("The local subscription mapping could not be reconciled with Maxio.")
    {
    }
}
