using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionResponse> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionResponse>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);
}

public sealed class SubscriptionService : ISubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    private readonly IMaxioBillingClient _maxio;
    private readonly AppIdentityDbContext _identityDb;
    private readonly MaxioOptions _options;

    public SubscriptionService(IMaxioBillingClient maxio, AppIdentityDbContext identityDb, IOptions<MaxioOptions> options)
    {
        _maxio = maxio;
        _identityDb = identityDb;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var plans = await _maxio.ListPlansAsync(_options.ProductFamilyHandle, cancellationToken);
        return plans
            .Where(plan => plan.ArchivedAt is null && !string.IsNullOrWhiteSpace(plan.Handle))
            .Select(plan => new SubscriptionPlanResponse(plan.Handle!, plan.Name, plan.PriceInCents, plan.Interval, plan.IntervalUnit))
            .ToList();
    }

    public async Task<SubscriptionResponse> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var normalizedPlanHandle = planHandle.Trim();
        var lockHandle = EnrollmentLocks.GetOrAdd($"{user.Id}:{normalizedPlanHandle}", _ => new SemaphoreSlim(1, 1));
        await lockHandle.WaitAsync(cancellationToken);
        try
        {
            // Validate that the requested plan belongs to the configured family; clients cannot subscribe to arbitrary site products.
            var plan = (await _maxio.ListPlansAsync(_options.ProductFamilyHandle, cancellationToken))
                .SingleOrDefault(candidate => candidate.ArchivedAt is null && string.Equals(candidate.Handle, normalizedPlanHandle, StringComparison.Ordinal));
            if (plan is null)
            {
                throw new SubscriptionValidationException("The requested plan is not available.");
            }

            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var enrollment = await _identityDb.SubscriptionEnrollments
                .SingleOrDefaultAsync(item => item.UserId == user.Id && item.ProductHandle == normalizedPlanHandle, cancellationToken);
            var existingSubscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

            if (enrollment?.MaxioSubscriptionId is long localSubscriptionId)
            {
                var locallyRecordedSubscription = existingSubscriptions.SingleOrDefault(item => item.Id == localSubscriptionId);
                if (locallyRecordedSubscription is not null)
                {
                    return ToResponse(locallyRecordedSubscription);
                }
            }

            var subscriptionReference = BuildSubscriptionReference(user.Id, normalizedPlanHandle);
            var alreadyCreatedSubscription = existingSubscriptions.SingleOrDefault(item =>
                string.Equals(item.Reference, subscriptionReference, StringComparison.Ordinal));
            if (alreadyCreatedSubscription is not null)
            {
                await SaveEnrollmentAsync(enrollment, user.Id, normalizedPlanHandle, alreadyCreatedSubscription.Id, cancellationToken);
                return ToResponse(alreadyCreatedSubscription);
            }

            // Reserve the user/plan pair before the remote write. The unique database index handles concurrent requests across processes.
            if (enrollment is null)
            {
                enrollment = new SubscriptionEnrollment { UserId = user.Id, ProductHandle = normalizedPlanHandle };
                _identityDb.SubscriptionEnrollments.Add(enrollment);
                try
                {
                    await _identityDb.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    _identityDb.Entry(enrollment).State = EntityState.Detached;
                    enrollment = await _identityDb.SubscriptionEnrollments.SingleAsync(
                        item => item.UserId == user.Id && item.ProductHandle == normalizedPlanHandle, cancellationToken);
                }
            }

            // Reference is retained by Maxio and is used to recover safely if a network failure occurs after creation.
            var createdSubscription = await _maxio.CreateSubscriptionAsync(customer.Id, normalizedPlanHandle, subscriptionReference, cancellationToken);
            await SaveEnrollmentAsync(enrollment, user.Id, normalizedPlanHandle, createdSubscription.Id, cancellationToken);
            return ToResponse(createdSubscription);
        }
        finally
        {
            lockHandle.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionResponse>> GetMySubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var customer = await _maxio.FindCustomerByReferenceAsync(BuildCustomerReference(user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionResponse>();
        }

        if (user.MaxioCustomerId != customer.Id)
        {
            user.MaxioCustomerId = customer.Id;
            await _identityDb.SaveChangesAsync(cancellationToken);
        }

        return (await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
            .Select(ToResponse)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(user.Id);
        var customer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            try
            {
                customer = await _maxio.CreateCustomerAsync(CreateCustomer(user, reference), cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                // The reference is unique per the Maxio contract. A concurrent creator may have won the race.
                var concurrentlyCreatedCustomer = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
                if (concurrentlyCreatedCustomer is null)
                {
                    throw;
                }

                customer = concurrentlyCreatedCustomer;
            }
        }

        if (user.MaxioCustomerId != customer.Id)
        {
            user.MaxioCustomerId = customer.Id;
            await _identityDb.SaveChangesAsync(cancellationToken);
        }

        return customer;
    }

    private async Task SaveEnrollmentAsync(SubscriptionEnrollment? enrollment, string userId, string planHandle, long subscriptionId, CancellationToken cancellationToken)
    {
        enrollment ??= new SubscriptionEnrollment { UserId = userId, ProductHandle = planHandle };
        if (_identityDb.Entry(enrollment).State == EntityState.Detached)
        {
            _identityDb.SubscriptionEnrollments.Add(enrollment);
        }

        enrollment.MaxioSubscriptionId = subscriptionId;
        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static MaxioCustomerCreate CreateCustomer(ApplicationUser user, string reference)
    {
        var email = user.Email ?? user.UserName ?? throw new SubscriptionValidationException("Your account does not have an email address.");
        var localPart = email.Split('@', 2)[0];
        return new MaxioCustomerCreate(string.IsNullOrWhiteSpace(localPart) ? "Shopper" : localPart, "Shopper", email, reference);
    }

    private void EnsureConfigured() => _ = _options.GetApiBaseUri();

    private static string BuildCustomerReference(string userId) => $"eshop-user-{userId}";
    private static string BuildSubscriptionReference(string userId, string planHandle) => $"eshop-subscription-{userId}-{planHandle}";

    private static SubscriptionResponse ToResponse(MaxioSubscription subscription) => new(
        subscription.Id,
        subscription.ProductHandle ?? string.Empty,
        subscription.ProductName,
        subscription.ProductPriceInCents,
        subscription.State,
        subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
}

public sealed class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(string message) : base(message) { }
}
