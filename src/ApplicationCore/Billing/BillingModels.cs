using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingPlan(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    string? ProductPricePointHandle);

public sealed record BillingCustomer(int Id, string Reference);

public sealed record BillingSubscription(
    int Id,
    string Reference,
    string ProductHandle,
    string ProductName,
    long? PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingAt);

public sealed record ShopperIdentity(
    string Subject,
    string Email,
    string FirstName,
    string LastName);

public enum BillingFailureKind
{
    Rejected,
    Unavailable,
    InvalidResponse,
    UnknownWriteOutcome
}

public sealed class BillingProviderException : Exception
{
    public BillingProviderException(
        BillingFailureKind kind,
        string safeMessage,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Kind = kind;
    }

    public BillingFailureKind Kind { get; }
    public bool IsAmbiguousWrite => Kind == BillingFailureKind.UnknownWriteOutcome;
}

public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<BillingPlan?> FindPlanAsync(string productHandle, CancellationToken cancellationToken);
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<BillingCustomer> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken);
    Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<BillingSubscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken);
}
