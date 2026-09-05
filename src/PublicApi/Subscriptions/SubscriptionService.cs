using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResponse> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private const string CustomerReferencePrefix = "eshoponweb-customer:";
    private const string SubscriptionReferencePrefix = "eshoponweb-subscription:";
    private readonly AppIdentityDbContext _identityContext;
    private readonly IMaxioClient _maxio;
    private readonly MaxioOptions _options;
    private readonly SubscriptionRequestLock _requestLock;

    public SubscriptionService(
        AppIdentityDbContext identityContext,
        IMaxioClient maxio,
        IOptions<MaxioOptions> options,
        SubscriptionRequestLock requestLock)
    {
        _identityContext = identityContext;
        _maxio = maxio;
        _options = options.Value;
        _requestLock = requestLock;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var plans = await _maxio.GetPlansAsync(cancellationToken);
        return plans.Select(MapPlan).ToList();
    }

    public async Task<SubscribeResponse> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new UnknownSubscriptionPlanException();
        }

        var plan = (await _maxio.GetPlansAsync(cancellationToken))
            .SingleOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new UnknownSubscriptionPlanException();
        }

        var subscriptionReference = GetSubscriptionReference(user.Id, plan.Handle);
        using var requestLock = await _requestLock.AcquireAsync(subscriptionReference, cancellationToken);

        var enrollment = await _identityContext.MaxioSubscriptionEnrollments
            .SingleOrDefaultAsync(item => item.UserId == user.Id && item.ProductHandle == plan.Handle, cancellationToken);

        var customer = await EnsureCustomerAsync(user, cancellationToken);
        var existingSubscription = await FindSubscriptionByReferenceAsync(customer.Id, subscriptionReference, cancellationToken);
        if (existingSubscription is not null)
        {
            await CompleteEnrollmentAsync(enrollment, user.Id, plan.Handle, subscriptionReference, existingSubscription.Id, cancellationToken);
            return new SubscribeResponse { Created = false, Subscription = MapSubscription(existingSubscription, plan) };
        }

        if (enrollment is not null)
        {
            // A different app instance has the durable claim. It may be awaiting the Maxio response;
            // do not issue another signup that could create a second recurring charge.
            throw new SubscriptionEnrollmentInProgressException();
        }

        enrollment = new MaxioSubscriptionEnrollment
        {
            UserId = user.Id,
            ProductHandle = plan.Handle,
            SubscriptionReference = subscriptionReference,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _identityContext.MaxioSubscriptionEnrollments.Add(enrollment);
        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _identityContext.ChangeTracker.Clear();
            throw new SubscriptionEnrollmentInProgressException();
        }

        try
        {
            var subscription = await _maxio.CreateSubscriptionAsync(customer.Id, plan.Handle, subscriptionReference, cancellationToken);
            enrollment.MaxioSubscriptionId = subscription.Id;
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
            await _identityContext.SaveChangesAsync(cancellationToken);
            return new SubscribeResponse { Created = true, Subscription = MapSubscription(subscription, plan) };
        }
        catch (MaxioIntegrationException exception) when (exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
        {
            // Maxio rejected the enrollment, so the durable claim must not prevent a corrected retry.
            _identityContext.MaxioSubscriptionEnrollments.Remove(enrollment);
            await _identityContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(GetCustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Where(subscription => string.Equals(subscription.Product?.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            .Select(subscription => MapSubscription(subscription, subscription.Product))
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var customerReference = GetCustomerReference(user.Id);
        var customer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new SubscriptionCustomerDataException();
        }

        var firstName = user.Email.Split('@', 2)[0];
        try
        {
            return await _maxio.CreateCustomerAsync(firstName, "Shopper", user.Email, customerReference, cancellationToken);
        }
        catch (MaxioIntegrationException exception) when (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // The reference is unique in Maxio. A concurrent request may have won the create race.
            var concurrentCustomer = await _maxio.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (concurrentCustomer is not null)
            {
                return concurrentCustomer;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(long customerId, string reference, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.GetCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.SingleOrDefault(subscription => string.Equals(subscription.Reference, reference, StringComparison.Ordinal));
    }

    private async Task CompleteEnrollmentAsync(
        MaxioSubscriptionEnrollment? enrollment,
        string userId,
        string productHandle,
        string subscriptionReference,
        long maxioSubscriptionId,
        CancellationToken cancellationToken)
    {
        if (enrollment is null)
        {
            enrollment = new MaxioSubscriptionEnrollment
            {
                UserId = userId,
                ProductHandle = productHandle,
                SubscriptionReference = subscriptionReference,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _identityContext.MaxioSubscriptionEnrollments.Add(enrollment);
        }

        enrollment.MaxioSubscriptionId = maxioSubscriptionId;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        BillingInterval = product.Interval,
        BillingIntervalUnit = product.IntervalUnit
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription subscription, MaxioProduct? fallbackProduct) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? fallbackProduct?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? fallbackProduct?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? fallbackProduct?.PriceInCents ?? 0,
        BillingInterval = subscription.Product?.Interval ?? fallbackProduct?.Interval ?? 0,
        BillingIntervalUnit = subscription.Product?.IntervalUnit ?? fallbackProduct?.IntervalUnit ?? string.Empty,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt
    };

    private static string GetCustomerReference(string userId) => $"{CustomerReferencePrefix}{userId}";
    private static string GetSubscriptionReference(string userId, string productHandle) => $"{SubscriptionReferencePrefix}{userId}:{productHandle}";
}

public sealed class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException() : base("The requested subscription plan is unavailable.") { }
}

public sealed class SubscriptionCustomerDataException : Exception
{
    public SubscriptionCustomerDataException() : base("An email address is required to enroll in a subscription.") { }
}

public sealed class SubscriptionEnrollmentInProgressException : Exception
{
    public SubscriptionEnrollmentInProgressException() : base("An enrollment for this plan is already in progress. Retry shortly.") { }
}
