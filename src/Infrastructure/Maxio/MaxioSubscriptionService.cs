using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements <see cref="ISubscriptionBillingService"/> on top of the Maxio Advanced
/// Billing API. Maxio is the system of record for customers and subscriptions; the
/// eShopOnWeb user id doubles as the Maxio customer "reference", which makes customer
/// provisioning idempotent (Maxio allows only one customer per reference value).
/// </summary>
public sealed class MaxioSubscriptionService : ISubscriptionBillingService
{
    private static readonly HashSet<string> s_liveStates = new(StringComparer.Ordinal)
    {
        "active",
        "trialing",
        "assessing",
        "on_hold",
        "past_due",
        "soft_failure"
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_subscribeLocks =
        new(StringComparer.Ordinal);

    private readonly MaxioApiClient _api;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioApiClient api, MaxioOptions options, ILogger<MaxioSubscriptionService> logger)
    {
        _api = api;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        _options.EnsureValid();
        var family = await ResolveProductFamilyAsync(cancellationToken);
        var products = await _api.ListProductsForFamilyAsync(family.Id, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(SubscriptionSignup signup, CancellationToken cancellationToken = default)
    {
        if (signup is null)
        {
            throw new ArgumentNullException(nameof(signup));
        }

        if (string.IsNullOrWhiteSpace(signup.CustomerReference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(signup));
        }

        if (string.IsNullOrWhiteSpace(signup.Email))
        {
            throw new ArgumentException("A customer email is required.", nameof(signup));
        }

        var planHandle = signup.PlanHandle?.Trim();
        if (string.IsNullOrEmpty(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(signup));
        }

        _options.EnsureValid();

        // Validate the plan exists and belongs to the configured catalog up-front so we
        // never create a Maxio customer for a plan the shopper cannot subscribe to.
        var plan = await _api.ReadProductByHandleAsync(planHandle, cancellationToken);
        if (plan is null || !BelongsToConfiguredFamily(plan))
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var customer = await EnsureCustomerAsync(signup, cancellationToken);

        // Serialize check-then-create per customer+plan so a double-click cannot create
        // two subscriptions. The lock is process-local (fine for a single app host).
        var lockKey = $"{signup.CustomerReference}\u0001{planHandle}";
        var gate = s_subscribeLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindLiveSubscriptionForPlanAsync(customer.Id, planHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Reusing existing Maxio subscription {SubscriptionId} for customer {CustomerId} and plan {PlanHandle}.",
                    existing.Id, customer.Id, planHandle);

                return new SubscriptionEnrollment
                {
                    IsNew = false,
                    Subscription = MapSubscription(existing)
                };
            }

            var created = await _api.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
                created.Id, customer.Id, planHandle);

            return new SubscriptionEnrollment
            {
                IsNew = true,
                Subscription = MapSubscription(created)
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return Array.Empty<SubscriptionDetails>();
        }

        _options.EnsureValid();

        var customer = await _api.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _api.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriptionSignup signup, CancellationToken cancellationToken)
    {
        var customer = await _api.FindCustomerByReferenceAsync(signup.CustomerReference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        try
        {
            return await _api.CreateCustomerAsync(
                signup.FirstName,
                signup.LastName,
                signup.Email,
                signup.CustomerReference,
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // Maxio enforces a single customer per reference. A 422 here almost always
            // means a concurrent request created the customer first; re-read by reference.
            var createdByPeer = await _api.FindCustomerByReferenceAsync(signup.CustomerReference, cancellationToken);
            if (createdByPeer is not null)
            {
                _logger.LogInformation(
                    "Maxio customer for reference {Reference} was created concurrently; reusing it.",
                    signup.CustomerReference);
                return createdByPeer;
            }

            throw;
        }
    }

    private bool BelongsToConfiguredFamily(MaxioProduct product)
    {
        return string.Equals(
            product.ProductFamily?.Handle,
            _options.ProductFamilyHandle,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<MaxioProductFamily> ResolveProductFamilyAsync(CancellationToken cancellationToken)
    {
        var families = await _api.ListProductFamiliesAsync(cancellationToken);
        var family = families.FirstOrDefault(f =>
            string.Equals(f.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            throw new MaxioConfigurationException(
                $"The configured product family handle '{_options.ProductFamilyHandle}' was not found in the Maxio site.");
        }

        return family;
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionForPlanAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _api.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            s_liveStates.Contains(s.State) &&
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product)
    {
        return new SubscriptionPlan
        {
            Id = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty,
            ProductFamilyName = product.ProductFamily?.Name ?? string.Empty,
            ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty,
            Archived = product.ArchivedAt is not null
        };
    }

    private static SubscriptionDetails MapSubscription(MaxioSubscription subscription)
    {
        return new SubscriptionDetails
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name ?? string.Empty,
            ProductPriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            BalanceInCents = subscription.BalanceInCents,
            TotalRevenueInCents = subscription.TotalRevenueInCents,
            Currency = subscription.Currency,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt
        };
    }
}
