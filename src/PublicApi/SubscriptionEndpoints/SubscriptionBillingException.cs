using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(int statusCode, string title, string safeMessage, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        StatusCode = statusCode;
        Title = title;
        SafeMessage = safeMessage;
    }

    public int StatusCode { get; }
    public string Title { get; }
    public string SafeMessage { get; }

    public static SubscriptionBillingException PlanNotFound(string handle) =>
        new(404, "Subscription plan not found", $"The subscription plan '{handle}' is not available.");

    public static SubscriptionBillingException Conflict(string message) =>
        new(409, "Subscription enrollment is in progress", message);

    public static SubscriptionBillingException Rejected(string message, Exception? inner = null) =>
        new(422, "Subscription was rejected", message, inner);

    public static SubscriptionBillingException Dependency(string message, Exception? inner = null) =>
        new(502, "Billing service unavailable", message, inner);

    public static SubscriptionBillingException Timeout(Exception? inner = null) =>
        new(504, "Billing service timeout", "Maxio did not respond within the allotted time.", inner);

    public static SubscriptionBillingException Configuration(string message, Exception? inner = null) =>
        new(503, "Billing configuration error", message, inner);
}
