using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Subscription states that mean "the customer already holds this
    /// subscription"; a double-click (or retry) must return it, not re-create it.
    /// </summary>
    private static readonly HashSet<string> NonTerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "trialing", "active", "annual", "on_trial", "past_due", "soft_failure", "unpaid", "suspended"
    };

    /// <summary>
    /// The seeded plans do not require a payment method at signup, so
    /// subscriptions are created with remittance collection (no card capture,
    /// no 3-DS) instead of the default automatic collection.
    /// </summary>
    private const string PaymentCollectionMethod = "remittance";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _maxioOptions;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly IReadRepository<Subscription> _subscriptionReadRepository;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioClient maxioClient,
        IOptions<MaxioOptions> maxioOptions,
        IRepository<Subscription> subscriptionRepository,
        IReadRepository<Subscription> subscriptionReadRepository,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _maxioOptions = maxioOptions.Value;
        _subscriptionRepository = subscriptionRepository;
        _subscriptionReadRepository = subscriptionReadRepository;
        _logger = logger;
    }

    public async Task<ListSubscriptionPlansResponse> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await ListActiveProductsAsync(cancellationToken);
        var defaultHandle = ResolveDefaultPlanHandle(products);

        var response = new ListSubscriptionPlansResponse
        {
            ProductFamilyHandle = _maxioOptions.ProductFamilyHandle
        };

        response.Plans.AddRange(products.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Price = CentsToPrice(p.PriceInCents),
            PriceInCents = p.PriceInCents,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            RequireCreditCard = p.RequireCreditCard,
            IsDefault = string.Equals(p.Handle, defaultHandle, StringComparison.OrdinalIgnoreCase)
        }));

        return response;
    }

    public async Task<SubscriptionDetailsDto> SubscribeAsync(string userId, string email, string firstName, string lastName,
        string? planHandle, CancellationToken cancellationToken = default)
    {
        // Serialize concurrent subscribes for the same user so a double-click
        // cannot race past the "already subscribed" check and create two
        // Maxio customers/subscriptions.
        var userLock = UserLocks.GetOrAdd(userId, static _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(userId, email, firstName, lastName, planHandle, cancellationToken);
        }
        finally
        {
            userLock.Release();
        }
    }

    private async Task<SubscriptionDetailsDto> SubscribeCoreAsync(string userId, string email, string firstName, string lastName,
        string? planHandle, CancellationToken cancellationToken)
    {
        var products = await ListActiveProductsAsync(cancellationToken);
        if (products.Count == 0)
        {
            throw new MaxioConfigurationException(
                $"No active plans were found in the Maxio product family '{_maxioOptions.ProductFamilyHandle}'.");
        }

        var product = ResolvePlan(products, planHandle);

        var customer = await _maxioClient.LookupCustomerByReferenceAsync(userId, cancellationToken);
        MaxioSubscription subscription;
        var alreadySubscribed = false;

        if (customer != null)
        {
            var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                s.Product?.Handle == product.Handle && NonTerminalStates.Contains(s.State));

            if (existing != null)
            {
                _logger.LogInformation(
                    "User {UserId} is already subscribed to plan {PlanHandle} (Maxio subscription {SubscriptionId}); returning it.",
                    userId, product.Handle, existing.Id);
                subscription = existing;
                alreadySubscribed = true;
            }
            else
            {
                subscription = await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
                {
                    UniquenessToken = Guid.NewGuid().ToString("N"),
                    Subscription = new MaxioCreateSubscriptionBody
                    {
                        ProductHandle = product.Handle,
                        PaymentCollectionMethod = PaymentCollectionMethod,
                        CustomerId = customer.Id
                    }
                }, cancellationToken);
            }
        }
        else
        {
            // Single atomic signup: creates the Maxio customer (referenced by the
            // eShopOnWeb user id) and the subscription in one call, so a retry
            // either reuses the found customer or is blocked by the uniqueness token.
            subscription = await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
            {
                UniquenessToken = Guid.NewGuid().ToString("N"),
                Subscription = new MaxioCreateSubscriptionBody
                {
                    ProductHandle = product.Handle,
                    PaymentCollectionMethod = PaymentCollectionMethod,
                    CustomerAttributes = new MaxioCustomerAttributes
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = userId
                    }
                }
            }, cancellationToken);
        }

        var customerId = subscription.Customer?.Id ?? customer?.Id ?? 0;
        await PersistAsync(userId, customerId, subscription, cancellationToken);

        return MapToDetails(subscription, alreadySubscribed);
    }

    public async Task<IReadOnlyList<SubscriptionDetailsDto>> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var result = new List<SubscriptionDetailsDto>();
        var seenSubscriptionIds = new HashSet<long>();

        var customer = await _maxioClient.LookupCustomerByReferenceAsync(userId, cancellationToken);
        if (customer != null)
        {
            var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            foreach (var subscription in subscriptions)
            {
                result.Add(MapToDetails(subscription, alreadySubscribed: true));
                seenSubscriptionIds.Add(subscription.Id);
            }
        }

        // Fall back to (and merge with) the persisted cross-reference so the
        // user's subscriptions remain visible even when the live lookup has
        // nothing to report yet.
        var local = await _subscriptionReadRepository.ListAsync(new SubscriptionsByUserIdSpecification(userId), cancellationToken);
        foreach (var subscription in local)
        {
            if (seenSubscriptionIds.Contains(subscription.MaxioSubscriptionId))
            {
                continue;
            }

            result.Add(new SubscriptionDetailsDto
            {
                MaxioSubscriptionId = subscription.MaxioSubscriptionId,
                MaxioCustomerId = subscription.MaxioCustomerId,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                Price = CentsToPrice(subscription.PriceInCents),
                State = subscription.State,
                NextBillingDateUtc = subscription.NextBillingDateUtc,
                CreatedAtUtc = subscription.CreatedUtc,
                AlreadySubscribed = true
            });
        }

        return result;
    }

    private async Task<List<MaxioProduct>> ListActiveProductsAsync(CancellationToken cancellationToken)
    {
        var family = await _maxioClient.GetProductFamilyByHandleAsync(_maxioOptions.ProductFamilyHandle, cancellationToken)
            ?? throw new MaxioConfigurationException(
                $"Maxio product family '{_maxioOptions.ProductFamilyHandle}' was not found on the configured site.");

        return (await _maxioClient.ListProductsForFamilyAsync(family.Handle, cancellationToken)).ToList();
    }

    private MaxioProduct ResolvePlan(List<MaxioProduct> products, string? planHandle)
    {
        var availableHandles = products.Select(p => p.Handle).ToList();

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            var defaultHandle = ResolveDefaultPlanHandle(products);
            if (defaultHandle == null)
            {
                throw new MaxioConfigurationException(
                    "No subscription plan was specified and no default plan could be resolved. " +
                    $"Available plans: {string.Join(", ", availableHandles)}. " +
                    "Pass a planHandle or configure Maxio:DefaultPlanHandle.");
            }

            return products.Single(p => p.Handle == defaultHandle);
        }

        var plan = products.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new MaxioPlanNotFoundException(planHandle, availableHandles);

        return plan;
    }

    private string? ResolveDefaultPlanHandle(List<MaxioProduct> products)
    {
        if (!string.IsNullOrWhiteSpace(_maxioOptions.DefaultPlanHandle))
        {
            return products.Any(p => string.Equals(p.Handle, _maxioOptions.DefaultPlanHandle, StringComparison.OrdinalIgnoreCase))
                ? _maxioOptions.DefaultPlanHandle
                : null;
        }

        // Catalog-agnostic default: when the family offers exactly one active plan, it is the default.
        return products.Count == 1 ? products[0].Handle : null;
    }

    private async Task PersistAsync(string userId, long maxioCustomerId, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _subscriptionReadRepository.FirstOrDefaultAsync(
                new SubscriptionByMaxioIdSpecification(subscription.Id), cancellationToken);

            if (existing != null)
            {
                existing.UpdateState(subscription.State, subscription.CurrentPeriodEndsAt);
                await _subscriptionRepository.UpdateAsync(existing, cancellationToken);
                await _subscriptionRepository.SaveChangesAsync(cancellationToken);
                return;
            }

            await _subscriptionRepository.AddAsync(new Subscription(
                userId,
                maxioCustomerId,
                subscription.Id,
                subscription.Product?.Handle ?? string.Empty,
                subscription.Product?.Name ?? string.Empty,
                subscription.Product?.PriceInCents ?? 0,
                subscription.State,
                subscription.CurrentPeriodEndsAt), cancellationToken);
            await _subscriptionRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // The local cross-reference is a convenience, not a source of truth:
            // Maxio already accepted the subscription, so never fail the request over it.
            _logger.LogError(ex, "Failed to persist local subscription snapshot for user {UserId}, Maxio subscription {SubscriptionId}.",
                userId, subscription.Id);
        }
    }

    private static SubscriptionDetailsDto MapToDetails(MaxioSubscription subscription, bool alreadySubscribed)
    {
        var product = subscription.Product ?? new MaxioProduct();
        return new SubscriptionDetailsDto
        {
            MaxioSubscriptionId = subscription.Id,
            MaxioCustomerId = subscription.Customer?.Id ?? 0,
            PlanHandle = product.Handle,
            PlanName = product.Name,
            Price = CentsToPrice(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            State = subscription.State,
            NextBillingDateUtc = subscription.CurrentPeriodEndsAt,
            CreatedAtUtc = subscription.CreatedAt,
            AlreadySubscribed = alreadySubscribed
        };
    }

    private static decimal CentsToPrice(int cents) => cents / 100m;
}
