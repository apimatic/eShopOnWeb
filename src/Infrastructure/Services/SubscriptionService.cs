using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Orchestrates recurring-subscription signup against Maxio Advanced Billing.
///
/// Idempotency guarantees (a double-click never creates two customers or two
/// subscriptions):
///  - the Maxio customer is keyed by the eShop user id used as the Maxio
///    customer "reference", and looked up before it is created;
///  - the Maxio subscription carries a deterministic reference derived from
///    (userId, productHandle); if Maxio rejects a duplicate, the existing
///    subscription is returned instead.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly HashSet<string> ResubscribableStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly IRepository<MaxioCustomerLink> _customerLinks;
    private readonly IRepository<MaxioSubscriptionRecord> _subscriptions;
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IRepository<MaxioCustomerLink> customerLinks,
        IRepository<MaxioSubscriptionRecord> subscriptions,
        IMaxioClient maxioClient,
        IOptions<MaxioOptions> options,
        IAppLogger<SubscriptionService> logger)
    {
        _customerLinks = customerLinks;
        _subscriptions = subscriptions;
        _maxioClient = maxioClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductFamilyProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null)
            .Select(ToPlanInfo)
            .OrderBy(p => p.Price)
            .ToList();
    }

    public async Task<SubscriptionDetails?> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var products = await _maxioClient.ListProductFamilyProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var product = products.FirstOrDefault(p =>
            string.Equals(p.Handle, command.ProductHandle, StringComparison.OrdinalIgnoreCase) && p.ArchivedAt is null);

        if (product is null)
        {
            _logger.LogWarning($"Subscription requested for unknown plan handle {command.ProductHandle}.");
            return null;
        }

        var existingRecords = await _subscriptions.ListAsync(
            new MaxioSubscriptionsByUserAndProductSpec(command.UserId, product.Handle!), cancellationToken);
        var liveRecord = existingRecords.FirstOrDefault(r => !ResubscribableStates.Contains(r.State));
        if (liveRecord is not null)
        {
            _logger.LogInformation($"User {command.UserId} already holds subscription {liveRecord.MaxioSubscriptionId} for plan {product.Handle}; returning it.");
            return ToDetails(liveRecord, await GetMaxioCustomerIdAsync(command.UserId, cancellationToken));
        }

        var customerId = await EnsureMaxioCustomerIdAsync(command, cancellationToken);

        var reference = BuildDeterministicReference(command.UserId, product.Handle!);
        MaxioSubscription subscription;
        try
        {
            subscription = await _maxioClient.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = product.Handle!,
                    CustomerId = customerId,
                    Reference = reference,
                    PaymentCollectionMethod = "remittance"
                },
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 && HasReferenceConflict(ex))
        {
            // The deterministic reference is already taken on the Maxio site.
            // If that subscription is still live, reuse it; only replace an
            // ended subscription with a fresh one (unique reference).
            var existing = await _maxioClient.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            if (!ResubscribableStates.Contains(existing.State ?? string.Empty))
            {
                subscription = existing;
            }
            else
            {
                reference = $"{reference}-{Guid.NewGuid():N}";
                subscription = await _maxioClient.CreateSubscriptionAsync(
                    new MaxioCreateSubscription
                    {
                        ProductHandle = product.Handle!,
                        CustomerId = customerId,
                        Reference = reference,
                        PaymentCollectionMethod = "remittance"
                    },
                    cancellationToken);
            }
        }

        var record = await UpsertRecordAsync(command.UserId, reference, product, subscription, cancellationToken);
        return ToDetails(record, customerId);
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken)
    {
        var records = await _subscriptions.ListAsync(new MaxioSubscriptionsByUserSpec(userId), cancellationToken);
        var customerId = await GetMaxioCustomerIdAsync(userId, cancellationToken);
        var results = new List<SubscriptionDetails>();

        foreach (var record in records)
        {
            try
            {
                var current = await _maxioClient.FindSubscriptionByReferenceAsync(record.MaxioReference, cancellationToken);
                if (current is not null)
                {
                    record.SyncFromMaxio(
                        current.Id,
                        current.ProductPriceInCents,
                        current.State ?? record.State,
                        current.CurrentPeriodEndsAt,
                        current.UpdatedAt);
                    await _subscriptions.UpdateAsync(record, cancellationToken);
                }
            }
            catch (MaxioApiException ex)
            {
                // Keep serving the cached record when Maxio lookup fails.
                _logger.LogWarning($"Maxio lookup for subscription reference {record.MaxioReference} failed: {ex.Message}");
            }

            results.Add(ToDetails(record, customerId));
        }

        return results;
    }

    private async Task<int> GetMaxioCustomerIdAsync(string userId, CancellationToken cancellationToken)
    {
        var link = (await _customerLinks.ListAsync(new MaxioCustomerLinkByUserIdSpec(userId), cancellationToken))
            .FirstOrDefault();
        return link?.MaxioCustomerId ?? 0;
    }

    private async Task<int> EnsureMaxioCustomerIdAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var link = (await _customerLinks.ListAsync(new MaxioCustomerLinkByUserIdSpec(command.UserId), cancellationToken))
            .FirstOrDefault();
        if (link is not null)
        {
            return link.MaxioCustomerId;
        }

        var existing = await _maxioClient.FindCustomerByReferenceAsync(command.UserId, cancellationToken);
        if (existing is not null)
        {
            await _customerLinks.AddAsync(new MaxioCustomerLink(command.UserId, existing.Id), cancellationToken);
            return existing.Id;
        }

        var (firstName, lastName) = DeriveName(command.UserName, command.Email);
        var created = await _maxioClient.CreateCustomerAsync(
            new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = command.Email ?? command.UserName,
                Reference = command.UserId
            },
            cancellationToken);

        _logger.LogInformation($"Created Maxio customer {created.Id} for user {command.UserId}.");
        await _customerLinks.AddAsync(new MaxioCustomerLink(command.UserId, created.Id), cancellationToken);
        return created.Id;
    }

    private async Task<MaxioSubscriptionRecord> UpsertRecordAsync(
        string userId, string reference, MaxioProduct product, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        var effectiveReference = subscription.Reference ?? reference;
        var records = await _subscriptions.ListAsync(
            new MaxioSubscriptionsByUserAndProductSpec(userId, product.Handle!), cancellationToken);
        var record = records.FirstOrDefault(r => string.Equals(r.MaxioReference, effectiveReference, StringComparison.Ordinal));

        if (record is null)
        {
            record = new MaxioSubscriptionRecord(
                userId,
                effectiveReference,
                subscription.Id,
                product.Handle!,
                product.Name ?? product.Handle!,
                subscription.ProductPriceInCents,
                subscription.State ?? "active",
                subscription.CurrentPeriodEndsAt,
                subscription.CreatedAt == default ? DateTimeOffset.UtcNow : subscription.CreatedAt);
            await _subscriptions.AddAsync(record, cancellationToken);
        }
        else
        {
            record.SyncFromMaxio(
                subscription.Id,
                subscription.ProductPriceInCents,
                subscription.State ?? record.State,
                subscription.CurrentPeriodEndsAt,
                subscription.UpdatedAt == default ? DateTimeOffset.UtcNow : subscription.UpdatedAt);
            await _subscriptions.UpdateAsync(record, cancellationToken);
        }

        return record;
    }

    private static bool HasReferenceConflict(MaxioApiException ex) =>
        ex.Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase));

    private static string BuildDeterministicReference(string userId, string productHandle) =>
        $"eshopweb-{userId}-{productHandle}";

    private static SubscriptionPlanInfo ToPlanInfo(MaxioProduct product) =>
        new(
            product.Id,
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            product.Description,
            product.PriceInCents,
            product.PriceInCents / 100m,
            product.Interval,
            product.IntervalUnit ?? string.Empty);

    private static SubscriptionDetails ToDetails(MaxioSubscriptionRecord record, int? maxioCustomerId) =>
        new(
            record.MaxioSubscriptionId,
            record.MaxioReference,
            maxioCustomerId ?? 0,
            record.State,
            record.ProductHandle,
            record.ProductName,
            record.PriceInCents,
            record.PriceInCents / 100m,
            record.NextBillingAt,
            null,
            null,
            record.CreatedAt);

    private static (string FirstName, string LastName) DeriveName(string userName, string? email)
    {
        var source = string.IsNullOrWhiteSpace(email) ? userName : email;
        var local = source.Split('@')[0];
        var tokens = local
            .Split(new[] { '.', '-', '_', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Select(t => char.ToUpperInvariant(t[0]) + t[1..])
            .ToArray();

        var firstName = tokens.Length > 0 ? tokens[0] : "eShop";
        var lastName = tokens.Length > 1 ? string.Join(" ", tokens.Skip(1)) : "Shopper";
        return (firstName, lastName);
    }
}
