using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Subscription billing orchestration backed by Maxio Advanced Billing.
/// Maxio is the billing system of record: the shop user ↔ Maxio customer
/// linkage is stored as the Maxio customer's "reference" (so it needs no
/// local database), and subscription idempotency is guaranteed with a
/// deterministic subscription "reference" per user + plan, guarded by a
/// per-user lock for concurrent requests.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    // The Maxio customer reference for a shop user is stable and unique.
    private const string CustomerReferencePrefix = "eshop-user-";
    private const string SubscriptionReferencePrefix = "eshop-sub-";

    // Subscription states that mean "the user already has this subscription":
    // replaying the subscribe call returns the existing subscription instead
    // of creating a duplicate. End-of-life states (canceled, expired, failed)
    // allow a fresh subscription to be created.
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "awaiting_signup", "awaiting_payment", "past_due", "on_hold", "pending_payment"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly IMaxioApiClient _maxio;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(IMaxioApiClient maxio, IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxio.ListProductsForProductFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products.Where(p => p.ArchivedAt is null).Select(MapPlan).ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string planHandle, string userId, string userName, string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required to subscribe.", nameof(planHandle));
        }
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A user id is required to subscribe.", nameof(userId));
        }

        var subscriptionReference = SubscriptionReference(userId, planHandle);

        // Serialize concurrent subscribe attempts for the same user + plan so a
        // double-click can never create two customers/subscriptions.
        var semaphore = SubscribeLocks.TryGetValue(LockKey(userId, planHandle), out var existingLock)
            ? existingLock
            : SubscribeLocks.GetOrAdd(LockKey(userId, planHandle), _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            // 1. Idempotent replay: an existing live subscription with the same
            //    deterministic reference is returned instead of creating a duplicate.
            var existingSubscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (existingSubscription is not null && LiveSubscriptionStates.Contains(existingSubscription.State))
            {
                return new SubscribeResult { Created = false, Subscription = MapSubscription(existingSubscription) };
            }

            // 2. Ensure a Maxio customer exists for the shop user (idempotent).
            var customerReference = CustomerReference(userId);
            var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                try
                {
                    customer = await _maxio.CreateCustomerAsync(BuildCustomer(userId, userName, email, customerReference), cancellationToken);
                }
                catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    // Lost a race with another node creating the same reference customer.
                    var fallback = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
                    if (fallback is null)
                    {
                        throw;
                    }
                    customer = fallback;
                }
            }

            // 3. Validate the plan belongs to the configured product family.
            var plans = await GetPlansAsync(cancellationToken);
            var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
                ?? throw new SubscriptionPlanNotFoundException(planHandle, _options.ProductFamilyHandle);

            // 4. Create the subscription. End-of-life replays get a fresh unique
            //    reference so Maxio keeps the full history.
            var referenceToUse = existingSubscription is not null
                ? $"{subscriptionReference}-{Guid.NewGuid():N}"
                : subscriptionReference;

            var subscription = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                Reference = referenceToUse,
                PaymentCollectionMethod = "remittance"
            }, cancellationToken);

            return new SubscribeResult { Created = true, Subscription = MapSubscription(subscription) };
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }

        var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(userId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionInfo>();
        }

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(MapSubscription)
            .ToList();
    }

    private static MaxioCreateCustomer BuildCustomer(string userId, string userName, string email, string customerReference)
    {
        var (firstName, lastName) = SplitName(userName, email);
        return new MaxioCreateCustomer
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Reference = customerReference
        };
    }

    private static (string FirstName, string LastName) SplitName(string userName, string email)
    {
        var source = string.IsNullOrWhiteSpace(email) ? userName : email;
        var localPart = (source ?? string.Empty).Split('@')[0];
        var nameParts = localPart.Split('.', '-', '_', ' ');
        var firstName = nameParts.Any() ? Capitalize(nameParts[0]) : "eShop";
        var lastName = nameParts.Length > 1 ? Capitalize(nameParts[^1]) : "Customer";
        return (firstName, lastName);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string CustomerReference(string userId) => $"{CustomerReferencePrefix}{userId}";

    private static string SubscriptionReference(string userId, string planHandle) => $"{SubscriptionReferencePrefix}{userId}-{planHandle.ToLowerInvariant()}";

    private static string LockKey(string userId, string planHandle) => $"{userId}|{planHandle.ToLowerInvariant()}";

    private static SubscriptionPlanInfo MapPlan(MaxioProduct product)
    {
        return new SubscriptionPlanInfo
        {
            ProductId = product.Id,
            Handle = product.Handle,
            Name = product.Name,
            Description = product.Description,
            Price = ToPrice(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            TrialPrice = product.TrialPriceInCents.HasValue ? ToPrice(product.TrialPriceInCents.Value) : null,
            TrialInterval = product.TrialInterval,
            TrialIntervalUnit = product.TrialIntervalUnit,
            RequiresPaymentMethod = product.RequireCreditCard,
            ProductFamilyHandle = product.ProductFamily.Handle
        };
    }

    private static SubscriptionInfo MapSubscription(MaxioSubscription subscription)
    {
        return new SubscriptionInfo
        {
            SubscriptionId = subscription.Id,
            Reference = subscription.Reference ?? string.Empty,
            PlanHandle = subscription.Product.Handle,
            PlanName = subscription.Product.Name,
            Price = ToPrice(subscription.ProductPriceInCents),
            State = subscription.State,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt
        };
    }

    private static decimal ToPrice(int cents) => cents / 100m;
}