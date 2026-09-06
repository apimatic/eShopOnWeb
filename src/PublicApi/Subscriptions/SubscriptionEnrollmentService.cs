using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionEnrollmentService
{
    Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionResponse> EnrollAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionResponse>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
}

/// <summary>
/// Coordinates the local idempotency records with Maxio, which is always queried for
/// customer and subscription state before a response is returned.
/// </summary>
public sealed class SubscriptionEnrollmentService : ISubscriptionEnrollmentService
{
    private const string CustomerReferencePrefix = "eshoponweb-user-";
    private const string SubscriptionReferencePrefix = "eshoponweb-subscription-";

    private readonly IMaxioBillingClient _maxio;
    private readonly AppIdentityDbContext _identityDb;
    private readonly SubscriptionEnrollmentGate _gate;
    private readonly MaxioOptions _options;

    public SubscriptionEnrollmentService(
        IMaxioBillingClient maxio,
        AppIdentityDbContext identityDb,
        SubscriptionEnrollmentGate gate,
        IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _identityDb = identityDb;
        _gate = gate;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await _maxio.ListProductsAsync(cancellationToken);
        return plans
            .Where(IsAvailablePlan)
            .OrderBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionResponse> EnrollAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionValidationException("planHandle is required.");
        }

        var normalizedPlanHandle = planHandle.Trim();
        await using var lease = await _gate.EnterAsync($"{user.Id}:{normalizedPlanHandle}", cancellationToken);

        var plans = await _maxio.ListProductsAsync(cancellationToken);
        var plan = plans.SingleOrDefault(product =>
            IsAvailablePlan(product) && string.Equals(product.Handle, normalizedPlanHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionValidationException("The requested plan is not available in the configured product family.");
        }

        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var existing = (await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
            .FirstOrDefault(subscription => string.Equals(subscription.Product?.Handle, normalizedPlanHandle, StringComparison.Ordinal));

        if (existing is not null)
        {
            await UpsertSubscriptionLinkAsync(user.Id, normalizedPlanHandle, existing.Id, cancellationToken);
            return MapSubscription(existing, plan);
        }

        var created = await _maxio.CreateSubscriptionAsync(
            normalizedPlanHandle,
            CustomerReference(user.Id),
            SubscriptionReference(user.Id, normalizedPlanHandle),
            cancellationToken);

        await UpsertSubscriptionLinkAsync(user.Id, normalizedPlanHandle, created.Id, cancellationToken);
        return MapSubscription(created, plan);
    }

    public async Task<IReadOnlyList<SubscriptionResponse>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionResponse>();
        }

        await UpsertCustomerLinkAsync(user.Id, customer, cancellationToken);
        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var response = new List<SubscriptionResponse>();
        foreach (var subscription in subscriptions.Where(subscription => subscription.Product?.Handle is not null && IsAvailablePlan(subscription.Product!)))
        {
            var plan = subscription.Product!;
            await UpsertSubscriptionLinkAsync(user.Id, plan.Handle!, subscription.Id, cancellationToken);
            response.Add(MapSubscription(subscription, plan));
        }

        return response.OrderByDescending(subscription => subscription.Id).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            try
            {
                customer = await _maxio.CreateCustomerAsync(reference, user.Email ?? user.UserName ?? $"{user.Id}@invalid.local", cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                // The reference is unique in Maxio. If another request won the race, read its customer.
                customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
                if (customer is null)
                {
                    throw;
                }
            }
        }

        await UpsertCustomerLinkAsync(user.Id, customer, cancellationToken);
        return customer;
    }

    private async Task UpsertCustomerLinkAsync(string userId, MaxioCustomer customer, CancellationToken cancellationToken)
    {
        var link = await _identityDb.MaxioCustomerLinks.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (link is null)
        {
            _identityDb.MaxioCustomerLinks.Add(new MaxioCustomerLink
            {
                UserId = userId,
                MaxioCustomerId = customer.Id,
                CustomerReference = customer.Reference ?? CustomerReference(userId),
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            link.MaxioCustomerId = customer.Id;
            link.CustomerReference = customer.Reference ?? CustomerReference(userId);
            link.UpdatedAt = now;
        }

        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertSubscriptionLinkAsync(string userId, string productHandle, long maxioSubscriptionId, CancellationToken cancellationToken)
    {
        var link = await _identityDb.MaxioSubscriptionLinks.SingleOrDefaultAsync(
            item => item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (link is null)
        {
            _identityDb.MaxioSubscriptionLinks.Add(new MaxioSubscriptionLink
            {
                UserId = userId,
                ProductHandle = productHandle,
                MaxioSubscriptionId = maxioSubscriptionId,
                SubscriptionReference = SubscriptionReference(userId, productHandle),
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            link.MaxioSubscriptionId = maxioSubscriptionId;
            link.UpdatedAt = now;
        }

        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private bool IsAvailablePlan(MaxioProduct product) =>
        !string.IsNullOrWhiteSpace(product.Handle) &&
        product.ArchivedAt is null &&
        string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal);

    private static SubscriptionPlanResponse MapPlan(MaxioProduct plan) => new()
    {
        Handle = plan.Handle!,
        Name = plan.Name,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    private static SubscriptionResponse MapSubscription(MaxioSubscription subscription, MaxioProduct fallbackPlan) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? fallbackPlan.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? fallbackPlan.Name,
        PriceInCents = subscription.ProductPriceInCents == 0 ? fallbackPlan.PriceInCents : subscription.ProductPriceInCents,
        State = subscription.State,
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
    };

    private static string CustomerReference(string userId) => CustomerReferencePrefix + userId;
    private static string SubscriptionReference(string userId, string planHandle) => SubscriptionReferencePrefix + userId + "-" + planHandle;
}

public sealed class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(string message) : base(message) { }
}

/// <summary>Per-process gate that serializes repeated checkout clicks for the same user and plan.</summary>
public sealed class SubscriptionEnrollmentGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable> EnterAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _released;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
