using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class BillingException : Exception
{
    public BillingException(HttpStatusCode statusCode, string title, string detail, Exception? innerException = null)
        : base(detail, innerException)
    {
        StatusCode = statusCode;
        Title = title;
    }

    public HttpStatusCode StatusCode { get; }
    public string Title { get; }

    public static BillingException InvalidRequest(string detail) =>
        new(HttpStatusCode.BadRequest, "Invalid subscription request", detail);

    public static BillingException Conflict(string detail) =>
        new(HttpStatusCode.Conflict, "Subscription request conflict", detail);

    public static BillingException NotFound(string detail) =>
        new(HttpStatusCode.NotFound, "Subscription plan not found", detail);

    public static BillingException ProviderUnavailable(Exception? innerException = null) =>
        new(HttpStatusCode.BadGateway, "Billing service unavailable",
            "The billing service could not complete the request. Try again later.", innerException);

    public static BillingException UnknownOutcome(Exception? innerException = null) =>
        new(HttpStatusCode.ServiceUnavailable, "Subscription status pending",
            "The billing service outcome is still being reconciled. Retry with the same idempotency key.", innerException);
}
