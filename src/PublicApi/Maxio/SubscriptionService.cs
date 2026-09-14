using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Identity of the eShopOnWeb user that maps 1:1 to a Maxio customer.
/// <see cref="Reference"/> is unique and stable (derived from the authenticated
/// user), which makes the customer lookup idempotent and stateless.
/// </summary>
public sealed class BillingShopper
{
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class SubscribeResult
{
    public SubscriptionDto Subscription { get; set; } = new();
    public bool Created { get; set; }
}

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(BillingShopper shopper, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(BillingShopper shopper, CancellationToken cancellationToken);
}

public class SubscriptionService : ISubscriptionService
{
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired"
    };

    private readonly IMaxioApiClient _maxioApiClient;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    public SubscriptionService(
        IMaxioApiClient maxioApiClient,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        ILogger<SubscriptionService> logger)
    {
        _maxioApiClient = maxioApiClient;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken cancellationToken)
    {
        return await _cache.GetOrCreateAsync(CacheKeyForPlans(), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);

            var products = await _maxioApiClient.ListProductsAsync(cancellationToken);
            var currency = await _maxioApiClient.GetSiteCurrencyAsync(cancellationToken);

            return products
                .Where(p => !p.ArchivedAt.HasValue)
                .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
                .Select(p => MapPlan(p, currency ?? string.Empty))
                .OrderBy(p => p.PriceInCents)
                .ToList();
        }) ?? new List<SubscriptionPlanDto>();
    }

    public async Task<SubscribeResult> SubscribeAsync(BillingShopper shopper, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var normalizedReference = NormalizeReference(shopper.Reference);
        var gate = _gates.GetOrAdd(normalizedReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(shopper, cancellationToken);
            var customerId = customer.Id
                ?? throw new MaxioApiException(200, "Maxio did not return a customer id.", null);

            var existing = await FindSubscriptionForPlanAsync(customerId, planHandle, cancellationToken);
            if (existing != null)
            {
                _logger.LogInformation("User {Reference} already has a subscription to plan {Plan}; returning the existing subscription {SubscriptionId}.",
                    normalizedReference, planHandle, existing.Id);
                return new SubscribeResult { Subscription = MapSubscription(existing), Created = false };
            }

            var plan = (await GetAvailablePlansAsync(cancellationToken))
                .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
                ?? throw new PlanNotFoundException(planHandle);

            var subscriptionReference = BuildSubscriptionReference(normalizedReference, planHandle);

            try
            {
                var created = await _maxioApiClient.CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest
                {
                    Subscription = new CreateMaxioSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customerId,
                        Reference = subscriptionReference,
                        NextBillingAt = plan.PriceInCents > 0 ? ComputeNextBillingDate(plan) : null
                    }
                }, cancellationToken);

                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {Reference} on plan {Plan}.",
                    created.Id, normalizedReference, planHandle);

                return new SubscribeResult { Subscription = MapSubscription(created), Created = true };
            }
            catch (MaxioApiException ex) when (ex.StatusCode == 422)
            {
                // Idempotency fallback: a concurrent request (e.g. in another
                // instance) may have created the subscription first. Return it.
                var concurrent = await FindSubscriptionForPlanAsync(customerId, planHandle, cancellationToken);
                if (concurrent != null)
                {
                    _logger.LogInformation("Concurrent subscription for user {Reference} on plan {Plan} detected ({SubscriptionId}); returning it.",
                        normalizedReference, planHandle, concurrent.Id);
                    return new SubscribeResult { Subscription = MapSubscription(concurrent), Created = false };
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(BillingShopper shopper, CancellationToken cancellationToken)
    {
        var customer = await _maxioApiClient.FindCustomerByReferenceAsync(NormalizeReference(shopper.Reference), cancellationToken);
        if (customer?.Id is not long customerId)
        {
            return new List<SubscriptionDto>();
        }

        var subscriptions = await _maxioApiClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .Where(s => s.State is not null && !EndOfLifeStates.Contains(s.State))
            .Select(MapSubscription)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(BillingShopper shopper, CancellationToken cancellationToken)
    {
        var reference = NormalizeReference(shopper.Reference);
        var existing = await _maxioApiClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(shopper.Email);
        try
        {
            var created = await _maxioApiClient.CreateCustomerAsync(new CreateMaxioCustomerRequest
            {
                Customer = new CreateMaxioCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = shopper.Email,
                    Reference = reference
                }
            }, cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for user reference {Reference}.",
                created.Id, reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // A concurrent request may have created the customer first.
            var found = await _maxioApiClient.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (found != null)
            {
                return found;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindSubscriptionForPlanAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxioApiClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            s.State is not null &&
            !EndOfLifeStates.Contains(s.State));
    }

    private SubscriptionPlanDto MapPlan(MaxioProduct product, string currency)
    {
        return new SubscriptionPlanDto
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents ?? 0,
            Currency = currency,
            Interval = product.Interval ?? 0,
            IntervalUnit = product.IntervalUnit ?? string.Empty
        };
    }

    private SubscriptionDto MapSubscription(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            ProductPriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            Currency = subscription.Currency,
            Interval = subscription.Product?.Interval,
            IntervalUnit = subscription.Product?.IntervalUnit,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CustomerId = subscription.Customer?.Id,
            CustomerEmail = subscription.Customer?.Email
        };
    }

    private static DateTimeOffset ComputeNextBillingDate(SubscriptionPlanDto plan)
    {
        var now = DateTimeOffset.UtcNow;
        var interval = Math.Max(plan.Interval, 1);
        return string.Equals(plan.IntervalUnit, "day", StringComparison.OrdinalIgnoreCase)
            ? now.AddDays(interval)
            : now.AddMonths(interval);
    }

    private static string BuildSubscriptionReference(string customerReference, string planHandle)
    {
        return $"{customerReference}:{planHandle}";
    }

    private static string NormalizeReference(string reference)
    {
        return reference.Trim().ToLowerInvariant();
    }

    private static (string First, string Last) SplitDisplayName(string email)
    {
        var localPart = email.Split('@', 2)[0];
        if (string.IsNullOrWhiteSpace(localPart))
        {
            return ("eShopOnWeb", "User");
        }

        var tokens = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2)
        {
            return (tokens[0], string.Join(" ", tokens.Skip(1)));
        }

        return (localPart, localPart);
    }

    private string CacheKeyForPlans()
    {
        return $"maxio:plans:{_options.ProductFamilyHandle}".ToLowerInvariant();
    }
}
