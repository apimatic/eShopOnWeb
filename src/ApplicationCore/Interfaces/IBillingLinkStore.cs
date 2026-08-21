using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IBillingLinkStore
{
    Task<CustomerClaim> ClaimCustomerAsync(string userId, string reference, DateTimeOffset now, CancellationToken cancellationToken);
    Task CompleteCustomerAsync(string userId, string leaseId, CancellationToken cancellationToken);
    Task FailCustomerAsync(string userId, string leaseId, bool retryable, string safeError, CancellationToken cancellationToken);
    Task UpsertRecoveredCustomerAsync(string userId, string reference, CancellationToken cancellationToken);

    Task<SubscriptionClaim> ClaimSubscriptionAsync(
        string userId,
        string productHandle,
        string reference,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task CompleteSubscriptionAsync(
        string userId,
        string productHandle,
        string leaseId,
        SubscriptionConfirmation confirmation,
        CancellationToken cancellationToken);
    Task FailSubscriptionAsync(
        string userId,
        string productHandle,
        string leaseId,
        bool retryable,
        string safeError,
        CancellationToken cancellationToken);
    Task UpsertRecoveredSubscriptionAsync(string userId, SubscriptionConfirmation confirmation, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscriptionLink>> ListSubscriptionsAsync(string userId, CancellationToken cancellationToken);
}

