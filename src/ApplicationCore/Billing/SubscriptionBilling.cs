using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingUser(
    string Id,
    string Email,
    string FirstName,
    string LastName);

public sealed record SubscriptionPlan(
    int MaxioProductId,
    string Name,
    string Handle,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record BillingCustomer(
    int MaxioCustomerId,
    string Reference);

public sealed record CustomerSubscription(
    int MaxioSubscriptionId,
    string? Reference,
    string PlanName,
    string PlanHandle,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate);

public interface ISubscriptionBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionPlan?> FindPlanAsync(string productHandle, CancellationToken cancellationToken);
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<BillingCustomer> CreateCustomerAsync(BillingUser user, string reference, CancellationToken cancellationToken);
    Task<CustomerSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<CustomerSubscription> CreateSubscriptionAsync(
        string productHandle,
        int maxioCustomerId,
        string reference,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        int maxioCustomerId,
        CancellationToken cancellationToken);
}

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<CustomerSubscription> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken);
}

public sealed class BillingProviderException : Exception
{
    public BillingProviderException(
        string safeMessage,
        int? providerStatusCode = null,
        bool outcomeMayBeUnknown = false,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        OutcomeMayBeUnknown = outcomeMayBeUnknown;
    }

    public int? ProviderStatusCode { get; }
    public bool OutcomeMayBeUnknown { get; }
}

public sealed class BillingRequestException : Exception
{
    public BillingRequestException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
