using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Orchestrates subscription enrollment with Maxio Advanced Billing as the system
/// of record. Customer provisioning is idempotent (stable per-buyer reference
/// verified unique by Maxio), and enrollment is guarded both locally and remotely
/// so a double-click can never create a duplicate subscription.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly HashSet<string> InactiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
    };

    private readonly IMaxioApiClient _maxio;
    private readonly IRepository<SubscriptionRecord> _repository;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(IMaxioApiClient maxio, IRepository<SubscriptionRecord> repository,
        IAppLogger<MaxioSubscriptionService> logger, IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _repository = repository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDetails>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await GetProductFamilyAsync(cancellationToken);
        var products = await _maxio.ListFamilyProductsAsync(family.Id, cancellationToken);
        var plans = products
            .Where(p => string.IsNullOrEmpty(p.ArchivedAt))
            .Select(p => new SubscriptionPlanDetails(p.Handle, p.Name, family.Handle,
                p.PriceInCents, p.Interval, p.IntervalUnit, p.Taxable))
            .OrderBy(p => p.PriceInCents)
            .ToList();
        _logger.LogInformation("Maxio returned {Count} active plans in product family {Family}.",
            plans.Count, family.Handle);
        return plans;
    }

    public async Task<SubscriptionDetails> SubscribeAsync(string buyerId, string productHandle, string email,
        string displayName, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        var reference = CustomerReferenceFor(buyerId);
        var customer = await GetOrCreateCustomerAsync(reference, buyerId, email, displayName, cancellationToken);
        Guard.Against.NegativeOrZero(customer.Id, nameof(customer.Id),
            "Maxio customer provisioning returned an invalid customer id.");

        var existingRecord = await FindRecordAsync(buyerId, productHandle, cancellationToken);
        if (existingRecord is not null && !IsInactive(existingRecord.State))
        {
            var refreshed = await RefreshRecordAsync(existingRecord, cancellationToken);
            if (refreshed)
            {
                _logger.LogInformation("Buyer {BuyerId} is already subscribed to {Product}; returning existing subscription {SubscriptionId}.",
                    buyerId, productHandle, existingRecord.MaxioSubscriptionId);
                return Map(existingRecord, alreadyExisted: true);
            }
            // Maxio no longer knows the subscription (or it went inactive); fall through and re-enroll.
        }

        // Remote guard: the local store may be empty (e.g. after a restart on an
        // ephemeral database) while Maxio already holds an active subscription.
        var remoteSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var remoteMatch = remoteSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
            !IsInactive(s.State));
        if (remoteMatch is not null)
        {
            var record = await UpsertRecordAsync(buyerId, reference, customer.Id, remoteMatch, existingRecord, cancellationToken);
            _logger.LogInformation("Maxio already holds active subscription {SubscriptionId} for buyer {BuyerId} on {Product}.",
                remoteMatch.Id, buyerId, productHandle);
            return Map(record, alreadyExisted: true);
        }

        var created = await _maxio.CreateSubscriptionAsync(customer.Id, productHandle, cancellationToken);
        var createdRecord = await UpsertRecordAsync(buyerId, reference, customer.Id, created, existingRecord, cancellationToken);
        _logger.LogInformation("Buyer {BuyerId} enrolled in {Product}: Maxio subscription {SubscriptionId} ({State}).",
            buyerId, productHandle, created.Id, created.State);
        return Map(createdRecord, alreadyExisted: false);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsForUserAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var records = await _repository.ListAsync(new SubscriptionsByBuyerSpecification(buyerId), cancellationToken);
        var details = new List<SubscriptionDetails>();
        foreach (var record in records)
        {
            if (!IsInactive(record.State))
            {
                await RefreshRecordAsync(record, cancellationToken);
            }
            details.Add(Map(record, alreadyExisted: true));
        }
        return details;
    }

    private async Task<MaxioProductFamily> GetProductFamilyAsync(CancellationToken cancellationToken)
    {
        var family = await _maxio.FindProductFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        if (family is null)
        {
            throw new InvalidOperationException(
                $"Maxio product family '{_options.ProductFamilyHandle}' was not found on the target site.");
        }
        return family;
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(string reference, string buyerId, string email,
        string displayName, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = SplitName(displayName, buyerId);
        try
        {
            customer = await _maxio.CreateCustomerAsync(reference, email, firstName, lastName, cancellationToken);
            _logger.LogInformation("Provisioned Maxio customer {CustomerId} (reference {Reference}) for buyer {BuyerId}.",
                customer.Id, reference, buyerId);
            return customer;
        }
        catch (MaxioApiException ex) when (ex.IsReferenceConflict)
        {
            // A concurrent request created the customer between lookup and create.
            customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (customer is not null)
            {
                _logger.LogInformation("Maxio customer for reference {Reference} was created concurrently; reusing it.", reference);
                return customer;
            }
            throw;
        }
    }

    private async Task<SubscriptionRecord?> FindRecordAsync(string buyerId, string productHandle,
        CancellationToken cancellationToken)
    {
        var records = await _repository.ListAsync(
            new SubscriptionByBuyerAndProductSpecification(buyerId, productHandle), cancellationToken);
        return records.FirstOrDefault();
    }

    /// <summary>Pulls fresh state from Maxio for a still-active local record.
    /// Returns false when Maxio no longer knows the subscription.</summary>
    private async Task<bool> RefreshRecordAsync(SubscriptionRecord record, CancellationToken cancellationToken)
    {
        var remote = await _maxio.GetSubscriptionAsync(record.MaxioSubscriptionId, cancellationToken);
        if (remote is null)
        {
            _logger.LogWarning("Maxio no longer returns subscription {SubscriptionId}; local record kept as-is.",
                record.MaxioSubscriptionId);
            return false;
        }
        record.RefreshFromBillingSystem(remote.State, remote.Product?.Name ?? record.ProductName,
            remote.ProductPriceInCents, remote.Product?.Interval ?? record.Interval,
            remote.Product?.IntervalUnit ?? record.IntervalUnit, remote.Currency,
            ParseDate(remote.NextAssessmentAt) ?? ParseDate(remote.CurrentPeriodEndsAt),
            ParseDate(remote.ActivatedAt), ParseDate(remote.CanceledAt));
        await _repository.UpdateAsync(record, cancellationToken);
        return IsInactive(record.State) ? false : true;
    }

    private async Task<SubscriptionRecord> UpsertRecordAsync(string buyerId, string reference, int customerId,
        MaxioSubscription subscription, SubscriptionRecord? existingRecord, CancellationToken cancellationToken)
    {
        if (existingRecord is not null)
        {
            existingRecord.RefreshFromBillingSystem(subscription.State, subscription.Product?.Name ?? "",
                subscription.ProductPriceInCents, subscription.Product?.Interval ?? 1,
                subscription.Product?.IntervalUnit ?? "month", subscription.Currency,
                ParseDate(subscription.NextAssessmentAt) ?? ParseDate(subscription.CurrentPeriodEndsAt),
                ParseDate(subscription.ActivatedAt), ParseDate(subscription.CanceledAt));
            await _repository.UpdateAsync(existingRecord, cancellationToken);
            return existingRecord;
        }

        var record = new SubscriptionRecord(buyerId, reference, customerId, subscription.Id,
            subscription.Product?.Handle ?? string.Empty, subscription.Product?.Name ?? string.Empty,
            subscription.ProductPriceInCents, subscription.Product?.Interval ?? 1,
            subscription.Product?.IntervalUnit ?? "month", subscription.State, subscription.Currency,
            ParseDate(subscription.NextAssessmentAt) ?? ParseDate(subscription.CurrentPeriodEndsAt),
            ParseDate(subscription.ActivatedAt));
        await _repository.AddAsync(record, cancellationToken);
        return record;
    }

    private static SubscriptionDetails Map(SubscriptionRecord record, bool alreadyExisted) =>
        new(record.BuyerId, record.CustomerReference, record.MaxioCustomerId, record.MaxioSubscriptionId,
            record.ProductHandle, record.ProductName, record.PriceInCents, record.Interval, record.IntervalUnit,
            record.State, record.Currency, record.NextBillingDate, record.ActivatedAt, record.CanceledAt,
            alreadyExisted);

    private static string CustomerReferenceFor(string buyerId) => $"eshoponweb:{buyerId}";

    private static bool IsInactive(string state) => InactiveStates.Contains(state);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static (string FirstName, string LastName) SplitName(string displayName, string fallback)
    {
        var trimmed = (displayName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            trimmed = fallback;
        }
        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("eShopOnWeb", "Customer"),
            1 => (parts[0], "Customer"),
            _ => (parts[0], parts[1]),
        };
    }
}
