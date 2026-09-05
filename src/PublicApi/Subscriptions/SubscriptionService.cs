using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Coordinates eShop identities with Maxio, the source of truth for billing state.</summary>
internal sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly AppIdentityDbContext _identityContext;
    private readonly IMaxioBillingClient _maxio;

    public SubscriptionService(AppIdentityDbContext identityContext, IMaxioBillingClient maxio)
    {
        _identityContext = identityContext;
        _maxio = maxio;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products.Select(ToPlan).OrderBy(x => x.Price).ToArray();
    }

    public async Task<SubscribeResponse> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        var normalizedHandle = planHandle.Trim();
        if (string.IsNullOrWhiteSpace(normalizedHandle)) throw new SubscriptionValidationException("planHandle is required.");

        var userLock = UserLocks.GetOrAdd(user.Id, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var plans = await GetPlansAsync(cancellationToken);
            var plan = plans.SingleOrDefault(x => string.Equals(x.Handle, normalizedHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null) throw new SubscriptionValidationException("The requested plan is not available.");

            var reference = SubscriptionReference(user.Id, plan.Handle);
            var mappedEnrollment = await _identityContext.MaxioSubscriptionEnrollments
                .SingleOrDefaultAsync(x => x.UserId == user.Id && x.PlanHandle == plan.Handle, cancellationToken);

            // A deterministic Maxio reference lets us recover after a network timeout and after an app restart.
            var existing = await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
            {
                await PersistEnrollmentAsync(user.Id, plan.Handle, reference, existing.Id, cancellationToken);
                return new SubscribeResponse { AlreadySubscribed = true, Subscription = ToSummary(existing) };
            }

            if (mappedEnrollment is not null)
            {
                // A stored enrollment that cannot be found remotely must not result in another charge/subscription.
                throw new SubscriptionConflictException("A subscription request for this plan is already being reconciled.");
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            if (customer is null) throw new InvalidOperationException("Unable to establish the Maxio customer mapping.");
            var created = await _maxio.CreateSubscriptionAsync(new CreateSubscription
            {
                CustomerId = customer.MaxioCustomerId,
                ProductHandle = plan.Handle,
                Reference = reference,
                // The supplied Maxio contract allows `invoice`; this supports the seeded
                // no-card catalog without sending any card data through eShop.
                PaymentCollectionMethod = "invoice"
            }, cancellationToken);

            await PersistEnrollmentAsync(user.Id, plan.Handle, reference, created.Id, cancellationToken);
            return new SubscribeResponse { AlreadySubscribed = false, Subscription = ToSummary(created) };
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(user, cancellationToken, createIfMissing: false);
        if (customer is null) return Array.Empty<SubscriptionSummary>();

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.MaxioCustomerId, cancellationToken);
        return subscriptions.Select(ToSummary).OrderByDescending(x => x.NextBillingDate).ToArray();
    }

    private async Task<Microsoft.eShopWeb.Infrastructure.Identity.MaxioCustomer?> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken, bool createIfMissing = true)
    {
        var mapped = await _identityContext.MaxioCustomers.FindAsync(new object?[] { user.Id }, cancellationToken);
        if (mapped is not null) return mapped;

        var reference = CustomerReference(user.Id);
        var maxioCustomer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (maxioCustomer is null && !createIfMissing) return null;

        if (maxioCustomer is null)
        {
            var (firstName, lastName) = SplitName(user.UserName ?? user.Email ?? user.Id);
            try
            {
                maxioCustomer = await _maxio.CreateCustomerAsync(new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = user.Email ?? user.UserName ?? $"{user.Id}@invalid.local",
                    Reference = reference
                }, cancellationToken);
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // `reference` is unique in Maxio. A competing request may have just created it.
                var concurrentlyCreated = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
                if (concurrentlyCreated is null) throw;
                maxioCustomer = concurrentlyCreated;
            }
        }

        var newMapping = new Microsoft.eShopWeb.Infrastructure.Identity.MaxioCustomer
        {
            UserId = user.Id,
            MaxioCustomerId = maxioCustomer.Id
        };
        _identityContext.MaxioCustomers.Add(newMapping);
        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _identityContext.ChangeTracker.Clear();
            var racedMapping = await _identityContext.MaxioCustomers.FindAsync(new object?[] { user.Id }, cancellationToken);
            if (racedMapping is null) throw;
            return racedMapping;
        }

        return newMapping;
    }

    private async Task PersistEnrollmentAsync(string userId, string planHandle, string reference, int subscriptionId, CancellationToken cancellationToken)
    {
        var enrollment = await _identityContext.MaxioSubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.UserId == userId && x.PlanHandle == planHandle, cancellationToken);
        if (enrollment is null)
        {
            _identityContext.MaxioSubscriptionEnrollments.Add(new MaxioSubscriptionEnrollment
            {
                UserId = userId,
                PlanHandle = planHandle,
                Reference = reference,
                MaxioSubscriptionId = subscriptionId
            });
        }
        else
        {
            enrollment.MaxioSubscriptionId = subscriptionId;
        }

        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _identityContext.ChangeTracker.Clear();
        }
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static SubscriptionSummary ToSummary(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product.Handle ?? string.Empty,
        PlanName = subscription.Product.Name,
        Price = subscription.ProductPriceInCents / 100m,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };

    private static string CustomerReference(string userId) => $"eshop-user:{userId}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-subscription:{userId}:{planHandle}";

    private static (string FirstName, string LastName) SplitName(string value)
    {
        var parts = value.Split(new[] { ' ', '@', '.', '_' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("eShop", "Shopper"),
            1 => (parts[0], "Shopper"),
            _ => (parts[0], parts[^1])
        };
    }
}

public sealed class SubscriptionValidationException : Exception { public SubscriptionValidationException(string message) : base(message) { } }
public sealed class SubscriptionConflictException : Exception { public SubscriptionConflictException(string message) : base(message) { } }
