using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public interface ISubscriptionLinkStore
{
    Task<MaxioCustomerLink?> FindCustomerAsync(string userId, CancellationToken cancellationToken);
    Task SaveCustomerAsync(MaxioCustomerLink customer, CancellationToken cancellationToken);
    Task<MaxioSubscriptionLink?> FindSubscriptionAsync(
        string userId,
        string productHandle,
        string pricePointHandle,
        CancellationToken cancellationToken);
    Task<SubscriptionClaim> ClaimSubscriptionAsync(
        string userId,
        string productHandle,
        string pricePointHandle,
        string subscriptionReference,
        Guid leaseId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task ConfirmSubscriptionAsync(
        MaxioSubscriptionLink link,
        Guid leaseId,
        int maxioSubscriptionId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task FailSubscriptionAsync(
        MaxioSubscriptionLink link,
        Guid leaseId,
        string safeErrorCode,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record SubscriptionClaim(MaxioSubscriptionLink Link, bool Acquired);
