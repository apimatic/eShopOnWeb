using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public sealed record SubscriptionPlanDto(
    string ProductHandle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    int Interval,
    string IntervalUnit,
    string? PricePointHandle,
    string? PricePointName);

public sealed record SubscriptionDto(
    int Id,
    string Reference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    decimal Price,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed record BillingCustomerProfile(string FirstName, string LastName, string Email);

public sealed record BillingCustomer(int Id, string Reference);

public sealed record CreateSubscriptionRequest(string ProductHandle);

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);

public sealed record SubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);

public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionPlanDto> GetPlanAsync(string productHandle, CancellationToken cancellationToken);
    Task<BillingCustomer> EnsureCustomerAsync(
        string customerReference,
        BillingCustomerProfile profile,
        CancellationToken cancellationToken);
    Task<SubscriptionDto?> FindSubscriptionAsync(string subscriptionReference, CancellationToken cancellationToken);
    Task<SubscriptionDto> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken);
}

public enum BillingProviderFailureKind
{
    Rejected,
    Unavailable,
    Protocol,
    OutcomeUnknown
}

public sealed class BillingProviderException : Exception
{
    public BillingProviderException(
        BillingProviderFailureKind kind,
        string safeMessage,
        HttpStatusCode? providerStatus = null,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Kind = kind;
        ProviderStatus = providerStatus;
    }

    public BillingProviderFailureKind Kind { get; }
    public HttpStatusCode? ProviderStatus { get; }
}

public sealed class SubscriptionEnrollmentInProgressException : Exception
{
    public SubscriptionEnrollmentInProgressException()
        : base("Subscription enrollment is still being processed. Retry shortly.")
    {
    }
}
