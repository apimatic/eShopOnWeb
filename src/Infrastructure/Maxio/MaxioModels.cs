using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The identity of the eShopOnWeb user that maps to a Maxio customer.
/// The user id is used as the unique Maxio customer <c>reference</c> so that
/// customer provisioning is idempotent.
/// </summary>
public sealed record SubscriberInfo(string UserId, string Email, string? FirstName, string? LastName);

/// <summary>
/// A subscription plan (Maxio product) available to eShopOnWeb shoppers.
/// </summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    int Interval,
    string? IntervalUnit,
    bool RequiresPaymentMethod);

/// <summary>
/// The state of a subscription as returned by Maxio, projected into the
/// eShopOnWeb domain.
/// </summary>
public sealed record SubscriptionDetails(
    long Id,
    string State,
    string? PlanHandle,
    string? PlanName,
    long PriceInCents,
    decimal Price,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt,
    bool CancelAtEndOfPeriod,
    bool AlreadySubscribed);

/// <summary>
/// Identifies the Maxio customer backing an eShopOnWeb user.
/// </summary>
public sealed record MaxioCustomerInfo(long MaxioCustomerId, string Reference, string? Email);

/// <summary>
/// Raised when the requested plan handle does not exist in the configured
/// product family.
/// </summary>
public sealed class MaxioPlanNotFoundException : Exception
{
    public MaxioPlanNotFoundException(string planHandle, string productFamilyHandle)
        : base($"Plan '{planHandle}' was not found in product family '{productFamilyHandle}'.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// Raised when the Maxio Billing API returns a non-success response.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(string requestUri, System.Net.HttpStatusCode statusCode, string responseBody)
        : base($"Maxio API request '{requestUri}' failed with HTTP {(int)statusCode}: {responseBody}")
    {
        RequestUri = requestUri;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public string RequestUri { get; }
    public System.Net.HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
}