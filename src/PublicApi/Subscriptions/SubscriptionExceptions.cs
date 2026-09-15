using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode)
        : base("Maxio Advanced Billing could not process the request.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException()
        : base("The requested subscription plan is unavailable.")
    {
    }
}

public class SubscriptionEnrollmentInProgressException : Exception
{
    public SubscriptionEnrollmentInProgressException()
        : base("The subscription enrollment is still being confirmed. Please try again shortly.")
    {
    }
}

public class SubscriptionUserProfileException : Exception
{
    public SubscriptionUserProfileException()
        : base("A confirmed email address is required before subscribing.")
    {
    }
}
