using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates subscription signups between eShopOnWeb users and Maxio Advanced
/// Billing. Maxio is the billing system of record; this service guarantees that
/// a single Maxio customer exists per application user and that repeated
/// subscribe attempts for the same user/plan never create duplicate subscriptions.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    // Serializes concurrent/double-click subscribe attempts for the same
    // owner+plan within the running process, on top of the persisted
    // (OwnerId, PlanHandle) uniqueness enforced by the database.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly IMaxioClient _maxioClient;
    private readonly IRepository<SubscriptionRecord> _repository;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioClient maxioClient,
        IRepository<SubscriptionRecord> repository,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        return await _maxioClient.GetProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
    }

    public async Task<MaxioSubscribeResult> SubscribeAsync(string ownerId, string userName, string email,
        string? planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("Owner id is required.", nameof(ownerId));

        var product = await ResolvePlanAsync(planHandle, cancellationToken);
        var lockKey = $"{ownerId}|{product.Handle}";
        var gate = SubscribeLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(ownerId, userName, email, product, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.GetCustomerByReferenceAsync(ownerId, cancellationToken);
        if (customer == null)
        {
            _logger.LogInformation("No Maxio customer found for reference {OwnerId}; user has no subscriptions.", ownerId);
            return Array.Empty<MaxioSubscription>();
        }

        return await _maxioClient.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioSubscribeResult> SubscribeCoreAsync(string ownerId, string userName, string email,
        MaxioProduct product, CancellationToken cancellationToken)
    {
        var planHandle = product.Handle!;

        // Idempotency guard #1: a persisted record for this owner+plan means the
        // user is already subscribed; refresh state from Maxio and return it.
        var existingRecords = await _repository.ListAsync(
            new SubscriptionRecordsByOwnerAndPlanSpecification(ownerId, planHandle), cancellationToken);
        var existingRecord = existingRecords.FirstOrDefault();
        if (existingRecord != null)
        {
            var existingSubscription = await _maxioClient.GetSubscriptionAsync(existingRecord.MaxioSubscriptionId, cancellationToken);
            if (existingSubscription != null)
            {
                _logger.LogInformation("User {OwnerId} is already subscribed to plan {PlanHandle} (Maxio subscription {SubscriptionId}).",
                    ownerId, planHandle, existingRecord.MaxioSubscriptionId);
                return new MaxioSubscribeResult
                {
                    Subscription = existingSubscription,
                    Product = product,
                    Customer = existingSubscription.Customer != null
                        ? new MaxioCustomer { Id = existingSubscription.Customer.Id, Reference = existingSubscription.Customer.Reference, Email = existingSubscription.Customer.Email }
                        : new MaxioCustomer { Id = existingRecord.MaxioCustomerId },
                    AlreadySubscribed = true,
                };
            }

            // Maxio no longer knows about the recorded subscription (e.g. site re-seed);
            // drop the stale mapping and re-enroll the user.
            await _repository.DeleteAsync(existingRecord, cancellationToken);
        }

        var customer = await EnsureCustomerAsync(ownerId, userName, email, cancellationToken);

        var subscription = await _maxioClient.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);

        try
        {
            await _repository.AddAsync(new SubscriptionRecord(ownerId, planHandle, customer.Id, subscription.Id), cancellationToken);
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            // A concurrent request won the insert; surface its subscription instead.
            _logger.LogWarning("Duplicate subscription insert raced for {OwnerId}/{PlanHandle}; returning the persisted subscription.", ownerId, planHandle);
            existingRecords = await _repository.ListAsync(
                new SubscriptionRecordsByOwnerAndPlanSpecification(ownerId, planHandle), cancellationToken);
            existingRecord = existingRecords.FirstOrDefault();
            if (existingRecord != null)
            {
                var racedSubscription = await _maxioClient.GetSubscriptionAsync(existingRecord.MaxioSubscriptionId, cancellationToken)
                    ?? subscription;
                return new MaxioSubscribeResult
                {
                    Subscription = racedSubscription,
                    Product = product,
                    Customer = customer,
                    AlreadySubscribed = true,
                };
            }
        }

        _logger.LogInformation("Subscribed user {OwnerId} to plan {PlanHandle}; Maxio subscription {SubscriptionId} created.",
            ownerId, planHandle, subscription.Id);

        return new MaxioSubscribeResult
        {
            Subscription = subscription,
            Product = product,
            Customer = customer,
            AlreadySubscribed = false,
        };
    }

    private async Task<MaxioProduct> ResolvePlanAsync(string? planHandle, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(planHandle))
        {
            var product = await _maxioClient.GetProductByHandleAsync(planHandle, cancellationToken);
            if (product == null || product.ArchivedAt != null)
            {
                throw new PlanNotFoundException(planHandle);
            }

            return product;
        }

        var plans = await GetPlansAsync(cancellationToken);
        var defaultPlan = plans.FirstOrDefault();
        if (defaultPlan == null)
        {
            throw new PlanNotFoundException(_options.ProductFamilyHandle);
        }

        return defaultPlan;
    }

    /// <summary>
    /// Guarantees exactly one Maxio customer per application user. The user's id is
    /// stored as the Maxio customer <c>reference</c>, which Maxio enforces as unique.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(string ownerId, string userName, string email, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.GetCustomerByReferenceAsync(ownerId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(userName, email);
        var toCreate = new MaxioCustomerCreate
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Reference = ownerId,
        };

        try
        {
            return await _maxioClient.CreateCustomerAsync(toCreate, cancellationToken);
        }
        catch (MaxioReferenceTakenException)
        {
            // Lost a race against a concurrent customer creation; the referenced
            // customer now exists and must be reused, never duplicated.
            var raced = await _maxioClient.GetCustomerByReferenceAsync(ownerId, cancellationToken);
            if (raced == null)
            {
                throw;
            }

            return raced;
        }
    }

    private static (string FirstName, string LastName) SplitName(string userName, string email)
    {
        var source = string.IsNullOrWhiteSpace(userName) ? email : userName;
        var localPart = source.Contains('@') ? source.Split('@')[0] : source;
        var parts = localPart.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0] : "eShop";
        var lastName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "Subscriber";
        return (firstName, lastName);
    }

    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        for (var current = (Exception?)ex; current != null; current = current.InnerException)
        {
            if (current.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}