using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements subscription enrollment against Maxio Advanced Billing, which is
/// the system of record for customers and subscriptions. A local
/// SubscriptionRecord is maintained for fast lookups and reporting.
///
/// Idempotency guarantees (a double-click never creates duplicates):
/// 1. The Maxio customer is keyed by the stable eShopOnWeb user id used as the
///    Maxio customer "reference"; the customer is looked up before it is
///    created, and a duplicate-reference rejection falls back to lookup.
/// 2. Before creating a subscription, both Maxio (by customer id + product
///    handle) and the local store (by user + plan) are checked for a live
///    subscription on the same plan; an existing one is returned instead.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired"
    };

    private readonly IMaxioClient _maxio;
    private readonly IRepository<SubscriptionRecord> _repository;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioClient maxio,
        IRepository<SubscriptionRecord> repository,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _maxio = maxio;
        _repository = repository;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync()
    {
        var products = await _maxio.ListFamilyProductsAsync();
        return products
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new SubscriptionPlanInfo
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = CentsToPrice(p.PriceInCents),
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                IsDefault = !string.IsNullOrWhiteSpace(_settings.DefaultPlanHandle) &&
                            string.Equals(p.Handle, _settings.DefaultPlanHandle, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string userId, string userName, string email, string? planHandle = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        var plan = await ResolvePlanAsync(planHandle);

        var customer = await GetOrCreateCustomerAsync(userId, userName, email);

        // Maxio is the source of truth: if the customer already holds a live
        // subscription for this plan, return it instead of creating another.
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id);
        var existing = subscriptions.FirstOrDefault(s =>
            s.Product != null &&
            string.Equals(s.Product.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            !TerminalStates.Contains(s.State));
        if (existing != null)
        {
            await UpsertRecordAsync(userId, userName, customer.Id, existing);
            _logger.LogInformation($"User {userName} already holds subscription {existing.Id} for plan {plan.Handle}.");
            return new SubscribeResult(MapSubscription(existing, customer.Id), alreadySubscribed: true);
        }

        // Local safety net (e.g. read-through while Maxio listing lags or local
        // data outlives what Maxio returned): a live local record for the same
        // user + plan means the subscription already exists.
        var localRecords = await _repository.ListAsync(new UserSubscriptionsSpecification(userId, plan.Handle));
        var liveRecord = localRecords.FirstOrDefault(r => !TerminalStates.Contains(r.State));
        if (liveRecord != null)
        {
            var fresh = await _maxio.GetSubscriptionAsync(liveRecord.MaxioSubscriptionId);
            if (fresh != null && !TerminalStates.Contains(fresh.State))
            {
                await UpsertRecordAsync(userId, userName, customer.Id, fresh);
                return new SubscribeResult(MapSubscription(fresh, customer.Id), alreadySubscribed: true);
            }
        }

        var created = await _maxio.CreateSubscriptionAsync(customer.Id, plan.Handle);
        await UpsertRecordAsync(userId, userName, customer.Id, created);
        _logger.LogInformation($"User {userName} subscribed to plan {plan.Handle}; Maxio subscription {created.Id} is {created.State}.");

        return new SubscribeResult(MapSubscription(created, customer.Id), alreadySubscribed: false);
    }

    public async Task<IReadOnlyList<UserSubscriptionInfo>> ListUserSubscriptionsAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentNullException(nameof(userId));
        }

        var customer = await _maxio.FindCustomerByReferenceAsync(userId);
        if (customer == null)
        {
            return Array.Empty<UserSubscriptionInfo>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id);

        foreach (var subscription in subscriptions)
        {
            try
            {
                await UpsertRecordAsync(userId, string.Empty, customer.Id, subscription);
            }
            catch (Exception ex)
            {
                // Record keeping must never break read operations.
                _logger.LogWarning($"Failed to persist local subscription record for Maxio subscription {subscription.Id}: {ex.Message}");
            }
        }

        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTime.MinValue)
            .Select(s => MapSubscription(s, customer.Id))
            .ToList();
    }

    private async Task<MaxioProduct> ResolvePlanAsync(string? planHandle)
    {
        var products = await _maxio.ListFamilyProductsAsync();
        var requested = planHandle;
        if (string.IsNullOrWhiteSpace(requested))
        {
            requested = _settings.DefaultPlanHandle;
        }

        MaxioProduct? plan = null;
        if (!string.IsNullOrWhiteSpace(requested))
        {
            plan = products.FirstOrDefault(p => string.Equals(p.Handle, requested, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            plan = products.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        if (plan == null)
        {
            throw new SubscriptionPlanNotFoundException(requested ?? "(none configured)");
        }

        return plan;
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(string userId, string userName, string email)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(userId);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveCustomerName(userName);
        try
        {
            return await _maxio.CreateCustomerAsync(new MaxioNewCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userId
            });
        }
        catch (MaxioApiException ex) when (IsDuplicateReferenceError(ex))
        {
            // Lost a creation race (e.g. concurrent double-click): the customer
            // now exists, so look it up instead of failing the request.
            var recovered = await _maxio.FindCustomerByReferenceAsync(userId);
            if (recovered != null)
            {
                return recovered;
            }
            throw;
        }
    }

    private static bool IsDuplicateReferenceError(MaxioApiException exception)
    {
        return exception.StatusCode is 422 or 400
            && exception.ParseErrors().Any(e =>
                e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
                e.Contains("taken", StringComparison.OrdinalIgnoreCase));
    }

    private static (string FirstName, string LastName) DeriveCustomerName(string userName)
    {
        var namePart = string.IsNullOrWhiteSpace(userName)
            ? "eShop"
            : (userName.Contains('@') ? userName.Split('@')[0] : userName);
        namePart = namePart.Length > 50 ? namePart[..50] : namePart;
        return string.IsNullOrWhiteSpace(namePart) ? ("eShop", "User") : (namePart, "User");
    }

    private async Task UpsertRecordAsync(string userId, string userName, long customerId, MaxioSubscription subscription)
    {
        if (subscription.Product == null)
        {
            return;
        }

        var records = await _repository.ListAsync(new UserSubscriptionsSpecification(userId));
        var record = records.FirstOrDefault(r => r.MaxioSubscriptionId == subscription.Id);

        if (record == null)
        {
            record = new SubscriptionRecord
            {
                UserId = userId,
                UserName = userName,
                CreatedAt = DateTime.UtcNow
            };
            record = await _repository.AddAsync(record);
        }

        record.UserName = string.IsNullOrWhiteSpace(record.UserName) ? userName : record.UserName;
        record.MaxioCustomerId = customerId;
        record.MaxioSubscriptionId = subscription.Id;
        record.PlanHandle = subscription.Product.Handle;
        record.PlanName = subscription.Product.Name;
        record.State = subscription.State;
        record.Currency = subscription.Currency;
        record.Price = CentsToPrice(subscription.ProductPriceInCents ?? subscription.Product.PriceInCents);
        record.NextBillingDate = subscription.NextAssessmentAt;
        record.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(record);
    }

    private static UserSubscriptionInfo MapSubscription(MaxioSubscription subscription, long customerId)
    {
        return new UserSubscriptionInfo
        {
            MaxioSubscriptionId = subscription.Id,
            MaxioCustomerId = customerId,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            State = subscription.State,
            Price = CentsToPrice(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0),
            Currency = subscription.Currency,
            NextBillingDate = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    private static decimal CentsToPrice(int cents) => cents / 100m;
}
