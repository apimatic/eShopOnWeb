using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements the subscription billing flows against Maxio Advanced Billing.
///
/// Idempotency strategy:
/// 1. A process-wide per-user lock serializes concurrent subscribe attempts
///    (double-clicks) past the existence checks.
/// 2. Maxio customers are keyed by the eShopOnWeb username as the Maxio
///    customer reference, so a customer is created at most once per user.
/// 3. Subscriptions get a deterministic app-side reference
///    "{username}:{planHandle}"; before creating, any existing subscription
///    with that reference is reused.
/// 4. The userId/plan-to-Maxio-subscription mapping is persisted locally so
///    "my subscriptions" queries do not depend on Maxio list filters.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // End-of-life subscription states: a new subscription is allowed.
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    { "canceled", "expired", "trial_ended", "failed_to_create", "on_hold" };

    private readonly MaxioClient _client;
    private readonly MaxioOptions _options;
    private readonly IRepository<MaxioSubscriptionRecord> _recordRepository;
    private readonly MaxioUserLockRegistry _lockRegistry;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioClient client,
        IOptions<MaxioOptions> options,
        IRepository<MaxioSubscriptionRecord> recordRepository,
        MaxioUserLockRegistry lockRegistry,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _recordRepository = recordRepository;
        _lockRegistry = lockRegistry;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(cancellationToken);

        return products
            .Select(p => new SubscriptionPlan(
                p.Handle ?? p.Id.ToString(),
                p.Name,
                p.Description,
                p.PriceInCents / 100m,
                p.IntervalUnit,
                p.Interval))
            .ToList();
    }

    public async Task<SubscriptionSummary> SubscribeAsync(string userId, string userName, string? email, string productHandle, CancellationToken cancellationToken = default)
    {
        var userLock = _lockRegistry.GetLock("user:" + userName);
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var plan = (await ListPlansAsync(cancellationToken))
                .FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.Ordinal))
                ?? throw new SubscriptionPlanNotFoundException(productHandle);

            var record = await FindRecordAsync(userId, productHandle, cancellationToken);
            if (record is not null)
            {
                var existing = await _client.ReadSubscriptionAsync(record.MaxioSubscriptionId, cancellationToken);
                if (existing is not null && !EndOfLifeStates.Contains(existing.State))
                {
                    _logger.LogInformation("User {UserName} is already subscribed to {ProductHandle} (Maxio subscription {SubscriptionId}).",
                        userName, productHandle, existing.Id);
                    return ToSummary(existing, record.MaxioSubscriptionReference);
                }
            }

            var reference = BuildReference(userName, productHandle);
            var lookedUp = await _client.LookupSubscriptionByReferenceAsync(reference, cancellationToken);
            if (lookedUp is not null && !EndOfLifeStates.Contains(lookedUp.State))
            {
                await SaveRecordAsync(userId, userName, lookedUp, cancellationToken);
                _logger.LogInformation("Reused existing Maxio subscription {SubscriptionId} for user {UserName} on {ProductHandle}.",
                    lookedUp.Id, userName, productHandle);
                return ToSummary(lookedUp, reference);
            }

            if (lookedUp is not null)
            {
                // The deterministic reference belongs to a dead subscription; make room for a fresh one.
                reference = $"{reference}:{DateTimeOffset.UtcNow.Ticks}";
            }

            var customer = await EnsureCustomerAsync(userName, email, cancellationToken);
            var subscription = await CreateSubscriptionSafeAsync(customer.Id, productHandle, reference, cancellationToken);

            await SaveRecordAsync(userId, userName, subscription, cancellationToken);

            _logger.LogInformation("User {UserName} subscribed to {ProductHandle}: Maxio subscription {SubscriptionId}, state {State}.",
                userName, productHandle, subscription.Id, subscription.State);

            return ToSummary(subscription, reference);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(string userId, string userName, CancellationToken cancellationToken = default)
    {
        var records = await _recordRepository.ListAsync(new MaxioSubscriptionRecordForUserSpecification(userId), cancellationToken);

        if (records.Count == 0)
        {
            // Local mapping lost (e.g. in-memory database restart): rebuild it
            // from the deterministic per-plan references stored in Maxio.
            records = await RebuildRecordsFromMaxioAsync(userId, userName, cancellationToken);
        }

        var summaries = new List<SubscriptionSummary>();
        foreach (var record in records)
        {
            var subscription = await _client.ReadSubscriptionAsync(record.MaxioSubscriptionId, cancellationToken);
            if (subscription is null)
            {
                _logger.LogWarning("Maxio subscription {SubscriptionId} referenced for user {UserName} no longer exists.",
                    record.MaxioSubscriptionId, userName);
                continue;
            }
            summaries.Add(ToSummary(subscription, record.MaxioSubscriptionReference));
        }

        return summaries;
    }

    private async Task<List<MaxioSubscriptionRecord>> RebuildRecordsFromMaxioAsync(string userId, string userName, CancellationToken cancellationToken)
    {
        var rebuilt = new List<MaxioSubscriptionRecord>();
        foreach (var plan in await ListPlansAsync(cancellationToken))
        {
            var reference = BuildReference(userName, plan.Handle);
            var subscription = await _client.LookupSubscriptionByReferenceAsync(reference, cancellationToken);
            if (subscription is null)
            {
                continue;
            }
            await SaveRecordAsync(userId, userName, subscription, cancellationToken);
            rebuilt.Add(new MaxioSubscriptionRecord
            {
                UserId = userId,
                UserName = userName,
                MaxioCustomerId = subscription.Customer?.Id ?? 0,
                MaxioSubscriptionId = subscription.Id,
                ProductHandle = plan.Handle,
                MaxioSubscriptionReference = reference
            });
        }
        return rebuilt;
    }

    private async Task<MaxioSubscriptionRecord?> FindRecordAsync(string userId, string productHandle, CancellationToken cancellationToken)
    {
        var records = await _recordRepository.ListAsync(
            new MaxioSubscriptionRecordForUserAndPlanSpecification(userId, productHandle), cancellationToken);
        return records.FirstOrDefault();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userName, string? email, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(userName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(userName);
        try
        {
            return await _client.CreateCustomerAsync(userName, firstName, lastName, email ?? $"{userName}@example.com", cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Lost a race with a concurrent creation for the same reference.
            var racedCustomer = await _client.LookupCustomerByReferenceAsync(userName, cancellationToken)
                ?? throw ex;
            return racedCustomer;
        }
    }

    private async Task<MaxioSubscription> CreateSubscriptionSafeAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.CreateSubscriptionAsync(customerId, productHandle, reference, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Possibly lost a race for the same deterministic subscription reference.
            var racedSubscription = await _client.LookupSubscriptionByReferenceAsync(reference, cancellationToken);
            if (racedSubscription is not null && !EndOfLifeStates.Contains(racedSubscription.State))
            {
                return racedSubscription;
            }
            throw;
        }
    }

    private async Task SaveRecordAsync(string userId, string userName, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        var existing = await FindRecordAsync(userId, subscription.Product?.Handle ?? string.Empty, cancellationToken);

        if (existing is not null && existing.MaxioSubscriptionId == subscription.Id)
        {
            return;
        }
        if (existing is not null)
        {
            await _recordRepository.DeleteAsync(existing, cancellationToken);
        }

        await _recordRepository.AddAsync(new MaxioSubscriptionRecord
        {
            UserId = userId,
            UserName = userName,
            MaxioCustomerId = subscription.Customer?.Id ?? 0,
            MaxioSubscriptionId = subscription.Id,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            MaxioSubscriptionReference = subscription.Reference ?? string.Empty,
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await _recordRepository.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionSummary ToSummary(MaxioSubscription subscription, string? reference) =>
        new(
            subscription.Id,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            (subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0) / 100m,
            subscription.State,
            subscription.CurrentPeriodEndsAt,
            subscription.ActivatedAt,
            subscription.Customer?.Id ?? 0,
            reference);

    private static string BuildReference(string userName, string productHandle) => $"{userName}:{productHandle}";

    private static (string FirstName, string LastName) SplitName(string userName)
    {
        var separatorIndex = userName.IndexOfAny(new[] { '@', '.', ' ', '_' });
        if (separatorIndex <= 0)
        {
            return (userName, "Subscriber");
        }
        var firstName = userName[..separatorIndex];
        var lastName = userName[(separatorIndex + 1)..];
        return (firstName, string.IsNullOrEmpty(lastName) ? "Subscriber" : lastName);
    }
}
