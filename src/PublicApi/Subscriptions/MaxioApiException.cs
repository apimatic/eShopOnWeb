using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode? statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found in the configured Maxio product family.")
    {
    }
}

public sealed class SubscriptionIdentityException : Exception
{
    public SubscriptionIdentityException() : base("The authenticated user could not be resolved.")
    {
    }
}

public sealed class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(string message) : base(message)
    {
    }
}

public sealed class SubscriptionReferenceConflictException : Exception
{
    public SubscriptionReferenceConflictException()
        : base("The idempotency reference is already associated with a different Maxio subscription.")
    {
    }
}
